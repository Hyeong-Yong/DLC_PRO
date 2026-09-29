using System.Collections.Generic;
using System.Globalization;
using DLC_PRO.Core;

namespace DLC_PRO.Models {
    /// <summary>콤보박스 항목 (정수 ID + 표시 문자열).</summary>
    public sealed class ChoiceItem {
        public ChoiceItem(int id, string text) {
            Id = id;
            Text = text;
        }

        public int Id { get; }
        public string Text { get; }

        public override string ToString() => Text;

        /// <summary>신호 채널 ID 목록 → "[id] 이름" 항목들.</summary>
        public static List<ChoiceItem> Channels(IEnumerable<int> ids) {
            List<ChoiceItem> list = new List<ChoiceItem>();
            foreach (int id in ids) list.Add(Channel(id));
            return list;
        }

        public static ChoiceItem Channel(int id) =>
            new ChoiceItem(id, "[" + id.ToString(CultureInfo.InvariantCulture) + "] " + SignalChannels.Name(id));

        /// <summary>"0=Sine", "1=Triangle" … 형식 → 항목들.</summary>
        public static List<ChoiceItem> Enum(params string[] idTextPairs) {
            List<ChoiceItem> list = new List<ChoiceItem>();
            foreach (string s in idTextPairs) {
                int eq = s.IndexOf('=');
                list.Add(new ChoiceItem(int.Parse(s.Substring(0, eq), CultureInfo.InvariantCulture), s.Substring(eq + 1)));
            }
            return list;
        }
    }
}
