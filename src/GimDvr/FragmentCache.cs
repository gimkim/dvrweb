using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
namespace GimDvr;

// One bounded, atomically published cache per RTSP reader; usable across web/worker processes.
public static class FragmentCache
{
    public const int MaximumPacket=16*1024*1024;
    public sealed record Entry(long Sequence,bool Keyframe,int Bytes);
    public sealed record Index(Entry[] Fragments);
    public static string Chunk(string root,long seq)=>Path.Combine(root,$"chunk{seq:D12}.m4s");
    public static string[] Arguments(int segmentMs=150)=>["-map","0:v:0","-c:v","copy","-bsf:v","extract_extradata","-an","-f","mp4","-movflags","empty_moov+delay_moov+default_base_moof+frag_keyframe","-frag_duration",(segmentMs*1000).ToString(System.Globalization.CultureInfo.InvariantCulture),"-flush_packets","1","pipe:1"];
    static uint U32(byte[] data,int offset)=>BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset,4));
    static IEnumerable<(string Type,int Body,int End)> Boxes(byte[] data,int start,int end)
    {
        for(int p=start;p<end;){
            if(end-p<8)throw new InvalidDataException("Truncated MP4 box");
            var size=U32(data,p);if(size<8||size>end-p)throw new InvalidDataException("Invalid MP4 box");
            yield return (Encoding.ASCII.GetString(data,p+4,4),p+8,p+(int)size);p+=(int)size;
        }
    }
    public static bool StartsWithKeyframe(byte[] moof)
    {
        foreach(var traf in Boxes(moof,8,moof.Length).Where(b=>b.Type=="traf")){
            uint? defaults=null;
            foreach(var box in Boxes(moof,traf.Body,traf.End)){
                if(box.Type=="tfhd"){
                    var flags=U32(moof,box.Body)&0xffffff;var p=box.Body+8;
                    if((flags&1)!=0)p+=8;if((flags&2)!=0)p+=4;if((flags&8)!=0)p+=4;if((flags&16)!=0)p+=4;
                    if((flags&32)!=0){if(p+4>box.End)throw new InvalidDataException("Invalid tfhd flags");defaults=U32(moof,p);}
                }
                if(box.Type=="trun"){
                    if(box.End-box.Body<8||U32(moof,box.Body+4)==0)continue;
                    var flags=U32(moof,box.Body)&0xffffff;var p=box.Body+8;if((flags&1)!=0)p+=4;
                    uint? first=defaults;
                    if((flags&4)!=0){if(p+4>box.End)throw new InvalidDataException("Invalid trun flags");first=U32(moof,p);}
                    else if((flags&0x400)!=0){if((flags&0x100)!=0)p+=4;if((flags&0x200)!=0)p+=4;if(p+4>box.End)throw new InvalidDataException("Invalid sample flags");first=U32(moof,p);}
                    return first is {} f&&(f&0x10000)==0&&((f>>24)&3)!=1;
                }
            }
        }
        return false;
    }
    // Some RTSP sources carry SPS/PPS only in-band even after delayed moov.
    // Fill an empty avcC from the first random-access packet before publishing init.
    public static byte[] CompleteInitialization(byte[] init,byte[] packet)
    {
        var parents=new List<int>();int avcc=-1;
        bool Find(int start,int end)
        {
            foreach(var box in Boxes(init,start,end)){
                int at=box.Body-8;
                if(box.Type=="avcC"){avcc=at;return true;}
                int skip=box.Type switch{"moov" or "trak" or "mdia" or "minf" or "stbl"=>0,"stsd"=>8,"avc1" or "avc3"=>78,_=>-1};
                if(skip>=0){parents.Add(at);if(Find(box.Body+skip,box.End))return true;parents.RemoveAt(parents.Count-1);}
            }
            return false;
        }
        if(!Find(0,init.Length))throw new InvalidDataException("Missing avcC");
        if(U32(init,avcc)>8)return init;
        byte[]? sps=null,pps=null;
        foreach(var box in Boxes(packet,0,packet.Length).Where(b=>b.Type=="mdat")){
            for(int p=box.Body;p<box.End;){
                if(p+4>box.End)throw new InvalidDataException("Truncated AVC NAL length");
                var length=U32(packet,p);p+=4;if(length==0||length>box.End-p)throw new InvalidDataException("Invalid AVC NAL length");
                var type=packet[p]&31;if(type==7&&sps is null)sps=packet.AsSpan(p,(int)length).ToArray();if(type==8&&pps is null)pps=packet.AsSpan(p,(int)length).ToArray();p+=(int)length;
            }
        }
        if(sps is null||pps is null||sps.Length<4||sps.Length>ushort.MaxValue||pps.Length>ushort.MaxValue)throw new InvalidDataException("Keyframe has no usable SPS/PPS");
        using var config=new MemoryStream();config.Write([1,sps[1],sps[2],sps[3],255,225]);
        void Parameter(byte[] nal){config.WriteByte((byte)(nal.Length>>8));config.WriteByte((byte)nal.Length);config.Write(nal);}
        Parameter(sps);config.WriteByte(1);Parameter(pps);var extra=config.ToArray();var result=new byte[init.Length+extra.Length];
        init.AsSpan(0,avcc+8).CopyTo(result);extra.CopyTo(result,avcc+8);init.AsSpan(avcc+8).CopyTo(result.AsSpan(avcc+8+extra.Length));
        foreach(var at in parents.Append(avcc))BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at,4),U32(init,at)+(uint)extra.Length);
        return result;
    }
    public static async Task Pump(Stream input,string root,CancellationToken ct=default)
    {
        Directory.CreateDirectory(root);using var init=new MemoryStream();byte[]? moof=null;bool initialized=false,publishedInit=false;long sequence=0,bytes=0;var entries=new Queue<Entry>();
        while(true){
            byte[] header=new byte[8];var first=await input.ReadAsync(header.AsMemory(0,1),ct);if(first==0)break;
            await input.ReadExactlyAsync(header.AsMemory(1),ct);var size=U32(header,0);
            if(size<8||size>MaximumPacket)throw new InvalidDataException("Unsupported MP4 box size");
            byte[] box=new byte[size];header.CopyTo(box,0);await input.ReadExactlyAsync(box.AsMemory(8),ct);
            var type=Encoding.ASCII.GetString(header,4,4);
            if(type is "ftyp" or "moov"){
                if(initialized||init.Length+box.Length>MaximumPacket)throw new InvalidDataException("Invalid MP4 initialization");
                init.Write(box);if(type=="moov")initialized=true;continue;
            }
            if(type=="moof"){if(!initialized||moof is not null)throw new InvalidDataException("Invalid fragment order");moof=box;continue;}
            if(type!="mdat")continue;
            if(moof is null||moof.Length+box.Length>MaximumPacket)throw new InvalidDataException("Invalid fragment payload");
            var keyframe=StartsWithKeyframe(moof);var packet=new byte[moof.Length+box.Length];moof.CopyTo(packet,0);box.CopyTo(packet,moof.Length);moof=null;
            if(!publishedInit){if(!keyframe)continue;await Atomic(Path.Combine(root,"init.mp4"),CompleteInitialization(init.ToArray(),packet),ct);publishedInit=true;}
            var entry=new Entry(++sequence,keyframe,packet.Length);await Atomic(Chunk(root,sequence),packet,ct);entries.Enqueue(entry);bytes+=packet.Length;
            var removed=new List<long>();while(entries.Count>128||bytes>64*1024*1024){var old=entries.Dequeue();bytes-=old.Bytes;removed.Add(old.Sequence);}
            await Atomic(Path.Combine(root,"index.json"),JsonSerializer.SerializeToUtf8Bytes(new Index(entries.ToArray())),ct);
            foreach(var old in removed)try{File.Delete(Chunk(root,old));}catch(IOException){}
        }
        if(moof is not null)throw new EndOfStreamException("Incomplete MP4 fragment");
    }
    static async Task Atomic(string path,byte[] bytes,CancellationToken ct)
    {
        await File.WriteAllBytesAsync(path+".tmp",bytes,ct);
        for(int attempt=0;;attempt++){try{File.Move(path+".tmp",path,true);return;}catch(Exception e) when(attempt<5 && (e is IOException or UnauthorizedAccessException)){await Task.Delay(20,ct);}}
    }
    public static Index? ReadIndex(string root)
    {
        try{using var file=new FileStream(Path.Combine(root,"index.json"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<Index>(file);}
        catch(IOException){return null;}catch(JsonException){return null;}
    }
    public static long? NextKeyframe(Index index,long after)=>index.Fragments.FirstOrDefault(f=>f.Sequence>after&&f.Keyframe)?.Sequence;
    public static async Task<byte[]> ReadPacket(string file,CancellationToken ct)
    {
        using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,true);
        if(input.Length<=0||input.Length>MaximumPacket)throw new InvalidDataException("Invalid fragment length");
        var result=new byte[input.Length];await input.ReadExactlyAsync(result,ct);return result;
    }
    public static async Task SendPacket(Stream output,byte[] data,CancellationToken ct)
    {
        if(data.Length==0||data.Length>MaximumPacket)throw new InvalidDataException("Invalid packet length");
        byte[] length=new byte[4];BinaryPrimitives.WriteInt32BigEndian(length,data.Length);await output.WriteAsync(length,ct);await output.WriteAsync(data,ct);await output.FlushAsync(ct);
    }
}
