# Move snapshot beside controls

User requests moving snapshot away from top-right video overlay. Overview snapshot now sits immediately beside Control in camera footer; single/fullscreen snapshot sits beside its existing Control in bottom action row. Viewer retains snapshot without PTZ access. Both buttons bind to same current video and clean up together. Removed absolute top-right positioning. Mode4/snapshot3/layout3 code checks passed; static deployment hashes matched. No browser/device testing, binary restart or recording changes.
