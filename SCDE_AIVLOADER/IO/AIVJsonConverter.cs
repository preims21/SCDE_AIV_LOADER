using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SCDE_AIVLOADER.IO
{
    public static class AivJsonConverter
    {
        // Converts an .aivjson file into a short[] packed like your fireflys hardcoded AIVs.
        public static short[] ConvertAivJsonFileToPackedArray(string aivJsonPath)
        {
            if (IsNullOrWhiteSpace(aivJsonPath))
                throw new ArgumentException("Path is required.", "aivJsonPath");

            string json = File.ReadAllText(aivJsonPath);

            AivRoot root = ParseAivJson(json);

            if (root == null)
                throw new InvalidDataException("Invalid or empty aivjson. First 200 chars: " + SafePreview(json, 200));

            if (root.frames == null)
                throw new InvalidDataException("aivjson missing 'frames'. First 200 chars: " + SafePreview(json, 200));

            List<short> packed = new List<short>(512);

            // Header
            packed.Add(CheckedShort(root.pauseDelayAmount));
            packed.Add(1);
            packed.Add(0);
            packed.Add(CheckedShort(root.frames.Length));

            // Frames
            foreach (AivFrame f in root.frames)
            {
                int itemType = f.itemType;
                int[] offsetsArr = f.tilePositionOfsets ?? new int[0];

                if (itemType == 25)
                {
                    if (offsetsArr.Length <= 1)
                    {
                        // single offset: 25, offset (or just 25 if missing offset)
                        packed.Add(25);
                        if (offsetsArr.Length == 1)
                            packed.Add(CheckedShort(offsetsArr[0]));
                    }
                    else
                    {
                        // multi offset list: -25, count, offsets...
                        packed.Add(-25);
                        packed.Add(CheckedShort(offsetsArr.Length));
                        for (int i = 0; i < offsetsArr.Length; i++)
                            packed.Add(CheckedShort(offsetsArr[i]));
                    }
                }
                else
                {
                    // General rule: encode as pairs (itemType, offset)
                    if (offsetsArr.Length == 0)
                        throw new InvalidDataException("Frame itemType=" + itemType + " has no tilePositionOfsets.");

                    for (int i = 0; i < offsetsArr.Length; i++)
                    {
                        packed.Add(CheckedShort(itemType));
                        packed.Add(CheckedShort(offsetsArr[i]));
                    }
                }
            }

            // Misc
            AivMisc[] misc = root.miscItems ?? new AivMisc[0];
            packed.Add(CheckedShort(misc.Length));

            for (int i = 0; i < misc.Length; i++)
            {
                AivMisc m = misc[i];
                packed.Add(CheckedShort(m.itemType));
                packed.Add(CheckedShort(m.positionOfset));
                packed.Add(CheckedShort(m.number));
            }

            return packed.ToArray();
        }

        /// <summary>
        /// Reverse of ConvertAivJsonFileToPackedArray: converts packed data back into an aivjson string.
        /// Notes:
        /// - shouldPause is not present in packed data, so it is emitted as false.
        /// - consecutive non-25 pairs with the same itemType are merged into one frame.
        /// </summary>
        public static string ConvertPackedArrayToAivJson(short[] packed, bool pretty = false)
        {
            if (packed == null) throw new ArgumentNullException("packed");
            if (packed.Length < 4) throw new InvalidDataException("Packed array too short (expected at least 4 header values).");

            int idx = 0;

            int pauseDelayAmount = packed[idx++];
            int magic1 = packed[idx++]; // expected 1
            int magic2 = packed[idx++]; // expected 0
            int framesCount = packed[idx++];

            if (magic1 != 1 || magic2 != 0)
            {
                // Not fatal, but helps catch wrong input early.
                throw new InvalidDataException("Packed header mismatch. Expected [pauseDelayAmount, 1, 0, framesCount].");
            }
            if (framesCount < 0)
                throw new InvalidDataException("Invalid framesCount: " + framesCount);

            var frames = new List<AivFrame>(framesCount);

            // Decode frames
            for (int f = 0; f < framesCount; f++)
            {
                if (idx >= packed.Length)
                    throw new InvalidDataException("Unexpected end of packed data while reading frames.");

                short token = packed[idx++];

                if (token == -25)
                {
                    if (idx >= packed.Length) throw new InvalidDataException("Unexpected end after -25.");
                    int count = packed[idx++];
                    if (count < 0) throw new InvalidDataException("Invalid -25 count: " + count);
                    if (idx + count > packed.Length) throw new InvalidDataException("Unexpected end while reading -25 offsets.");

                    int[] offsets = new int[count];
                    for (int i = 0; i < count; i++)
                        offsets[i] = packed[idx++];

                    frames.Add(new AivFrame
                    {
                        itemType = 25,
                        tilePositionOfsets = offsets,
                        shouldPause = false
                    });
                }
                else if (token == 25)
                {
                    // Ambiguous case: packer can emit just "25" with no offset if JSON had none.
                    // In practice aivJSON uses offsets, so we treat next value as offset if available.
                    if (idx < packed.Length)
                    {
                        int offset = packed[idx++];
                        frames.Add(new AivFrame
                        {
                            itemType = 25,
                            tilePositionOfsets = new[] { offset },
                            shouldPause = false
                        });
                    }
                    else
                    {
                        frames.Add(new AivFrame
                        {
                            itemType = 25,
                            tilePositionOfsets = new int[0],
                            shouldPause = false
                        });
                    }
                }
                else
                {
                    // General: (itemType, offset)
                    int itemType = token;
                    if (idx >= packed.Length)
                        throw new InvalidDataException("Unexpected end after itemType=" + itemType);

                    var offsets = new List<int>(4);
                    offsets.Add(packed[idx++]);

                    // Merge consecutive pairs with same itemType (cannot perfectly reconstruct original frame boundaries)
                    while (idx + 1 <= packed.Length - 1)
                    {
                        short nextType = packed[idx];
                        if (nextType != itemType) break;
                        idx++; // consume type
                        offsets.Add(packed[idx++]); // consume offset
                    }

                    frames.Add(new AivFrame
                    {
                        itemType = itemType,
                        tilePositionOfsets = offsets.ToArray(),
                        shouldPause = false
                    });
                }
            }

            // Decode misc
            if (idx >= packed.Length)
                throw new InvalidDataException("Unexpected end of packed data before miscCount.");

            int miscCount = packed[idx++];
            if (miscCount < 0)
                throw new InvalidDataException("Invalid miscCount: " + miscCount);

            if (idx + (miscCount * 3) > packed.Length)
                throw new InvalidDataException("Unexpected end of packed data while reading miscItems.");

            var miscItems = new AivMisc[miscCount];
            for (int i = 0; i < miscCount; i++)
            {
                miscItems[i] = new AivMisc
                {
                    itemType = packed[idx++],
                    positionOfset = packed[idx++],
                    number = packed[idx++]
                };
            }

            // Build JSON (no external JSON libs)
            return BuildAivJson(pauseDelayAmount, frames, miscItems, pretty);
        }

        public static void WriteAivJsonFileFromPackedArray(string outputPath, short[] packed, bool pretty = true)
        {
            if (IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", "outputPath");

            string json = ConvertPackedArrayToAivJson(packed, pretty);
            File.WriteAllText(outputPath, json);
        }

        private static string BuildAivJson(int pauseDelayAmount, List<AivFrame> frames, AivMisc[] miscItems, bool pretty)
        {
            string nl = pretty ? "\n" : "";
            string ind1 = pretty ? "  " : "";
            string ind2 = pretty ? "    " : "";
            string ind3 = pretty ? "      " : "";
            string sp = pretty ? " " : "";

            var sb = new StringBuilder(64 * 1024);

            sb.Append("{").Append(nl);

            sb.Append(ind1).Append("\"pauseDelayAmount\"").Append(":").Append(sp).Append(pauseDelayAmount).Append(",").Append(nl);

            sb.Append(ind1).Append("\"frames\"").Append(":").Append(sp).Append("[").Append(nl);
            for (int i = 0; i < frames.Count; i++)
            {
                AivFrame f = frames[i];
                sb.Append(ind2).Append("{").Append(nl);

                sb.Append(ind3).Append("\"itemType\"").Append(":").Append(sp).Append(f.itemType).Append(",").Append(nl);

                sb.Append(ind3).Append("\"tilePositionOfsets\"").Append(":").Append(sp).Append("[");
                int[] offs = f.tilePositionOfsets ?? new int[0];
                for (int j = 0; j < offs.Length; j++)
                {
                    if (j != 0) sb.Append(",").Append(sp);
                    sb.Append(offs[j]);
                }
                sb.Append("]").Append(",").Append(nl);

                sb.Append(ind3).Append("\"shouldPause\"").Append(":").Append(sp).Append("false").Append(nl);

                sb.Append(ind2).Append("}");
                if (i != frames.Count - 1) sb.Append(",");
                sb.Append(nl);
            }
            sb.Append(ind1).Append("]").Append(",").Append(nl);

            sb.Append(ind1).Append("\"miscItems\"").Append(":").Append(sp).Append("[").Append(nl);
            miscItems = miscItems ?? new AivMisc[0];
            for (int i = 0; i < miscItems.Length; i++)
            {
                AivMisc m = miscItems[i];
                sb.Append(ind2).Append("{").Append(nl);

                sb.Append(ind3).Append("\"positionOfset\"").Append(":").Append(sp).Append(m.positionOfset).Append(",").Append(nl);
                sb.Append(ind3).Append("\"itemType\"").Append(":").Append(sp).Append(m.itemType).Append(",").Append(nl);
                sb.Append(ind3).Append("\"number\"").Append(":").Append(sp).Append(m.number).Append(nl);

                sb.Append(ind2).Append("}");
                if (i != miscItems.Length - 1) sb.Append(",");
                sb.Append(nl);
            }
            sb.Append(ind1).Append("]").Append(nl);

            sb.Append("}");

            return sb.ToString();
        }

        private static AivRoot ParseAivJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            var root = new AivRoot();

            // pauseDelayAmount
            var pauseMatch = Regex.Match(json, "\"pauseDelayAmount\"\\s*:\\s*(-?\\d+)", RegexOptions.CultureInvariant);
            if (pauseMatch.Success)
                root.pauseDelayAmount = ParseIntSafe(pauseMatch.Groups[1].Value, 0);

            // Frames: be tolerant about key order within each frame object.
            // We match each {...} that contains "itemType":N and "tilePositionOfsets":[...]
            // and optionally "shouldPause":true/false (default false if missing).
            var frameMatches = Regex.Matches(
                json,
                "\\{(?=[^\\}]*\"itemType\"\\s*:\\s*(\\d+))(?=[^\\}]*\"tilePositionOfsets\"\\s*:\\s*\\[(.*?)\\])[^\\}]*?(?:\"shouldPause\"\\s*:\\s*(true|false))?[^\\}]*\\}",
                RegexOptions.CultureInvariant | RegexOptions.Singleline);

            if (frameMatches.Count > 0)
            {
                var frames = new AivFrame[frameMatches.Count];
                for (int i = 0; i < frameMatches.Count; i++)
                {
                    var m = frameMatches[i];

                    int itemType = ParseIntSafe(m.Groups[1].Value, 0);
                    int[] offsets = ParseIntArray(m.Groups[2].Value);

                    bool shouldPause = false;
                    if (m.Groups.Count >= 4 && m.Groups[3] != null && m.Groups[3].Success)
                        shouldPause = string.Equals(m.Groups[3].Value, "true", StringComparison.OrdinalIgnoreCase);

                    frames[i] = new AivFrame
                    {
                        itemType = itemType,
                        tilePositionOfsets = offsets,
                        shouldPause = shouldPause
                    };
                }

                root.frames = frames;
            }

            // Misc items
            var miscMatches = Regex.Matches(
                json,
                "\\{\\s*\"positionOfset\"\\s*:\\s*(-?\\d+)\\s*,\\s*\"itemType\"\\s*:\\s*(-?\\d+)\\s*,\\s*\"number\"\\s*:\\s*(-?\\d+)\\s*\\}",
                RegexOptions.CultureInvariant | RegexOptions.Singleline);

            if (miscMatches.Count > 0)
            {
                var misc = new AivMisc[miscMatches.Count];
                for (int i = 0; i < miscMatches.Count; i++)
                {
                    var m = miscMatches[i];
                    misc[i] = new AivMisc
                    {
                        positionOfset = ParseIntSafe(m.Groups[1].Value, 0),
                        itemType = ParseIntSafe(m.Groups[2].Value, 0),
                        number = ParseIntSafe(m.Groups[3].Value, 0)
                    };
                }
                root.miscItems = misc;
            }

            return root;
        }

        private static int ParseIntSafe(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        private static int[] ParseIntArray(string csv)
        {
            if (string.IsNullOrEmpty(csv))
                return new int[0];

            string[] parts = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<int>(parts.Length);

            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                int v;
                if (int.TryParse(p, out v))
                    result.Add(v);
            }

            return result.ToArray();
        }

        private static string SafePreview(string s, int maxLen)
        {
            if (s == null) return "<null>";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            return s.Length <= maxLen ? s : s.Substring(0, maxLen);
        }

        private static short CheckedShort(int value)
        {
            if (value < short.MinValue || value > short.MaxValue)
                throw new OverflowException("Value " + value + " does not fit into Int16.");
            return (short)value;
        }

        private static bool IsNullOrWhiteSpace(string s)
        {
            if (s == null) return true;
            for (int i = 0; i < s.Length; i++)
                if (!char.IsWhiteSpace(s[i]))
                    return false;
            return true;
        }

        // --- Models for the .aivjson structure ---

        [Serializable]
        public sealed class AivRoot
        {
            public int pauseDelayAmount;
            public AivFrame[] frames;
            public AivMisc[] miscItems;
        }

        [Serializable]
        public sealed class AivFrame
        {
            public int itemType;
            public int[] tilePositionOfsets;
            public bool shouldPause;
        }

        [Serializable]
        public sealed class AivMisc
        {
            public int positionOfset;
            public int itemType;
            public int number;
        }
    }
}