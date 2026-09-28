import importlib.util,pathlib,sys,unittest,types,numpy as np
from unittest.mock import patch
root=pathlib.Path('src/GimDvr/Detection');sys.path.insert(0,str(root.resolve()))
import onnx_person as p
class Tests(unittest.TestCase):
 def test_preprocess(self):
  frame=np.zeros((320,544,3),np.uint8);frame[:,:,0]=50
  out=p.tensor_for(frame)
  self.assertEqual(out.shape,(1,3,416,416));self.assertEqual(out.dtype,np.float32)
  self.assertEqual(out[0,0,0,0],50);self.assertEqual(out[0,1,0,0],0);self.assertEqual(out[0,0,-1,-1],114)
 def test_person_class(self):
  out=np.zeros((1,2,85));out[0,0,4]=.8;out[0,0,5]=.75;out[0,1,4]=1;out[0,1,6]=1
  self.assertAlmostEqual(p.person_score(out),.6)
 def test_invalid(self):
  out=np.full((1,1,85),np.nan)
  with self.assertRaises(ValueError):p.person_score(out)
 def test_cuda_failure_falls_back_cpu(self):
  class Session:
   def __init__(self,model,sess_options,providers):self.cuda=isinstance(providers[0],tuple)
   def get_providers(self):return ['CUDAExecutionProvider','CPUExecutionProvider'] if self.cuda else ['CPUExecutionProvider']
   def get_inputs(self):return [types.SimpleNamespace(name='input')]
   def run(self,*args):
    if self.cuda:raise RuntimeError('fixture CUDA failure')
    output=np.zeros((1,1,85));output[0,0,4:6]=[.8,.9];return [output]
  fake=types.SimpleNamespace(SessionOptions=types.SimpleNamespace,InferenceSession=Session,preload_dlls=lambda **kw:None)
  with patch.dict(sys.modules,{'onnxruntime':fake}),patch.dict('os.environ',{'MOTION_CUDA_DLL_DIRECTORY':''}):
   detector=p.OnnxPerson('fixture','CUDA');self.assertEqual(detector.device,'CPU');self.assertIsNone(detector.error);self.assertAlmostEqual(detector.score(np.zeros((320,544,3),np.uint8)),.72)
unittest.main()
