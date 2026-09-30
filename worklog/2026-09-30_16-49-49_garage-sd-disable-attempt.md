# Attempt to disable Garage SD recording

User asks to test disabling camera SD recording as a possible cause of stream freezes. Reviewed locally held Eye4 decompiled settings flow: read recording plans using trans_cmd_string.cgi cmd2017 command11 type3 (scheduled recording) /type1 (motion recording); corresponding writes command3 /command1 with preserved plan values and enable flag. Legacy SD recording settings also use set_recordsch.cgi. Vendor implementation was inspected only; not committed.

Garage UDP discovery reported HTTP65383, but authenticated read-only get_params.cgi requests repeatedly timed out, including bounded retry batches. No original SD settings could be retrieved. Therefore no SD write was sent, no SD format/delete occurred, and disabling SD has NOT been accomplished. User asked whether Eye4 storage settings remain accessible; response pending. NAS recording remains enabled. Source cause remains unresolved, not attributed to SD or Wi-Fi without evidence.
