using System;
using System.Collections.Generic;

namespace PcmSuite.Tuning.Core.Models
{
    /// <summary>
    /// Detects P01/P59 BIN images and corrects all 8 segment checksums in-place.
    /// Algorithm mirrors PCMHammer's FileValidator.ValidateRangeWordSum.
    /// </summary>
    public static class ChecksumCalculator
    {
        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Corrects all segment checksums in the supplied image.
        /// Modifies <paramref name="image"/> in-place.
        /// Returns a list of human-readable status lines (one per segment).
        /// If the image is not a recognised type, returns a single info line and does nothing.
        /// </summary>
        public static IReadOnlyList<string> CorrectChecksums(byte[] image)
        {
            if (!IsP01P59(image))
            {
                return new List<string> { "Checksum: not P01/P59 — no correction applied." };
            }

            return CorrectSegmentTable(image);
        }

        /// <summary>
        /// Corrects the 8-segment checksum table (at 0x50C) of a P01 or P59 image, whatever its size.
        /// PCMHammer treats both types identically (a P59 is a 1 MB image). The caller has already
        /// identified the type: this does not check it.
        /// </summary>
        public static IReadOnlyList<string> CorrectSegmentTable(byte[] image)
        {
            var messages = new List<string>();

            const int  Segments     = 8;

            for (uint seg = 0; seg < Segments; seg++)
            {
                if (!TryReadSegmentEntry(image, seg, out uint checksumAddr, out uint dataStart, out uint rawEnd))
                {
                    messages.Add($"Seg {seg}: address out of range, skipped.");
                    continue;
                }

                ushort oldCsum = (ushort)((image[checksumAddr] << 8) | image[checksumAddr + 1]);
                // PCMHammer uses two's complement: stored value is (0 - wordSum) so the
                // total of all words in the segment (including the checksum word) sums to zero.
                ushort newCsum = unchecked((ushort)(0 - ComputeWordSum(image, dataStart, rawEnd, checksumAddr)));

                image[checksumAddr]     = (byte)(newCsum >> 8);
                image[checksumAddr + 1] = (byte)(newCsum & 0xFF);

                string verdict = oldCsum == newCsum ? "OK (unchanged)" : $"fixed 0x{oldCsum:X4} → 0x{newCsum:X4}";
                messages.Add($"Seg {seg} @ 0x{checksumAddr:X5}: {verdict}");
            }

            return messages;
        }

        /// <summary>Returns true if the image looks like a P01 or P59 (512 KB, 0x4A/0xFC signatures).</summary>
        public static bool IsP01P59(byte[] image)
        {
            if (image.Length != 512 * 1024) return false;
            return image[0x1FFFE] == 0x4A && image[0x1FFFF] == 0xFC &&
                   image[0x7FFFE] == 0x4A && image[0x7FFFF] == 0xFC;
        }

        private static readonly string[] SegmentNames =
        {
            "Operating System",
            "Engine Calibration",
            "Engine Diagnostics",
            "Transmission Calibration",
            "Transmission Diagnostics",
            "Fuel System",
            "System",
            "Speedometer",
        };

        private static readonly string[] SegmentDescriptions =
        {
            "Core PCM firmware — hardware drivers, memory management, OS services. Only swap to change OS version. Incompatible OS can brick the PCM.",
            "Primary tuning area — fuel, ignition, VE, spark tables, boost control, and air management. Safe to swap between compatible calibrations.",
            "OBD2 diagnostic thresholds and enable/disable conditions for engine DTCs. Swap to change fault sensitivity or disable unwanted codes.",
            "Transmission shift points, line pressure, TCC lockup, and torque management tables.",
            "OBD2 diagnostic thresholds for transmission DTCs (shift solenoid faults, TCC slip, etc.).",
            "Fuel trim, long-term adaptation, and injector compensation data.",
            "PCM system configuration — VIN, immobiliser seed/key, hardware options, and configuration flags.",
            "Vehicle speed sensor calibration and speedometer scaling for tyre/gear changes.",
        };

        /// <summary>
        /// Reads the segment table from a P01/P59 image and returns SegmentInfo for each segment.
        /// Returns an empty list if the image is not recognised.
        /// </summary>
        public static IReadOnlyList<SegmentInfo> ReadSegments(byte[] image, bool requireP01Signature = true)
        {
            var result = new List<SegmentInfo>();
            if (requireP01Signature ? !IsP01P59(image) : image.Length < 0x600) return result;

            const int  Segments     = 8;

            for (uint seg = 0; seg < Segments; seg++)
            {
                if (!TryReadSegmentEntry(image, seg, out uint checksumAddr, out uint dataStart, out uint rawEnd)) continue;

                ushort csum = (ushort)((image[checksumAddr] << 8) | image[checksumAddr + 1]);

                result.Add(new SegmentInfo
                {
                    Index           = (int)seg,
                    Name            = seg < SegmentNames.Length ? SegmentNames[seg] : $"Segment {seg}",
                    Description     = seg < SegmentDescriptions.Length ? SegmentDescriptions[seg] : "",
                    StartAddress    = dataStart,
                    EndAddress      = rawEnd,
                    ChecksumAddress = checksumAddr,
                    TargetChecksum  = $"0x{csum:X4}",
                });
            }
            return result;
        }

        // ── Internal helpers ──────────────────────────────────────────

        /// <summary>
        /// 16-bit accumulating word sum over [start, end] (inclusive, step 2),
        /// skipping the word at <paramref name="storageAddr"/> and the
        /// P01/P59 factory area 0x4000–0x1FFFE (jumps to 0x20000).
        /// </summary>
        private static ushort ComputeWordSum(byte[] image, uint start, uint end, uint storageAddr)
        {
            ushort sum = 0;
            uint   addr = start;

            while (addr <= end)
            {
                // Skip the stored checksum word
                if (addr == storageAddr)
                {
                    addr += 2;
                    continue;
                }

                // P01/P59: skip the unprogrammed factory area
                if (addr == 0x4000)
                {
                    addr = 0x20000;
                    continue;
                }

                if (addr > end || addr + 1 >= (uint)image.Length) break;

                unchecked { sum += (ushort)((image[addr] << 8) | image[addr + 1]); }
                addr += 2;
            }

            return sum;
        }

        /// <summary>
        /// One row of the segment table: where its checksum word is, where its data starts and where it ends. False when any of those
        /// lies outside the image, or the table itself does (an erased table of 0xFF bytes, say). The arithmetic is done in 64 bits:
        /// in 32 bits a start of 0xFFFFFFFF wraps round to 1 and passes the range check.
        /// </summary>
        private static bool TryReadSegmentEntry(byte[] image, uint seg, out uint checksumAddr, out uint dataStart, out uint rawEnd)
        {
            const uint TableAddress = 0x50C;

            checksumAddr = 0;
            dataStart = 0;
            rawEnd = 0;

            long startLoc = TableAddress + seg * 8L;
            if (startLoc + 8 > image.Length) return false;

            uint rawStart = ReadUInt32BE(image, (uint)startLoc);
            rawEnd = ReadUInt32BE(image, (uint)startLoc + 4);

            // Segment 0: the checksum lives at 0x500 and the data starts at 0.
            // All others: the checksum is the first word of the segment.
            long checksum = rawStart == 0 ? 0x500L : rawStart;
            long start    = rawStart == 0 ? 0L     : (long)rawStart + 2;

            if (start >= image.Length || rawEnd >= image.Length || checksum + 1 >= image.Length) return false;

            checksumAddr = (uint)checksum;
            dataStart    = (uint)start;
            return true;
        }

        private static uint ReadUInt32BE(byte[] buf, uint offset)
            => (uint)((buf[offset] << 24) | (buf[offset + 1] << 16) |
                      (buf[offset + 2] << 8) | buf[offset + 3]);
    }
}
