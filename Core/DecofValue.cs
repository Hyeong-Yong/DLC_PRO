#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DLC_PRO.Core
{
    /// <summary>장비가 "Error: -n ..."을 반환했거나 통신 규약 위반 시 발생.</summary>
    public class DecofException : Exception
    {
        public int Code { get; private set; }
        public DecofException(string message, int code = 0) : base(message) { Code = code; }
        public DecofException(string message, Exception inner) : base(message, inner) { }

        /// <summary>"Error: -3 no such parameter" 형태의 문자열을 해석.</summary>
        public static DecofException FromErrorLine(string context, string line)
        {
            int code = 0;
            string rest = line.Trim();
            if (rest.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(6).Trim();
            int sp = rest.IndexOf(' ');
            string first = sp > 0 ? rest.Substring(0, sp) : rest;
            int c;
            if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out c)) code = c;
            return new DecofException(context + ": " + line.Trim(), code);
        }
    }

    /// <summary>
    /// DeCoF(Scheme) 값의 인코딩/디코딩 유틸리티 (Command Reference 2.1).
    /// 정수, 실수, 문자열("..."), 불리언(#t/#f), 튜플((a b)), 바이너리(&amp;BASE64 또는 "BASE64").
    /// </summary>
    public static class DecofValue
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------- 인코딩 ----------
        public static string Encode(object value)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value is bool) return ((bool)value) ? "#t" : "#f";
            if (value is double) return EncodeDouble((double)value);
            if (value is float) return EncodeDouble((float)value);
            if (value is decimal) return EncodeDouble((double)(decimal)value);
            if (value is int || value is long || value is short || value is byte)
                return Convert.ToInt64(value, Inv).ToString(Inv);
            if (value is string) return EncodeString((string)value);
            if (value is byte[]) return "\"" + Convert.ToBase64String((byte[])value) + "\"";
            if (value is RawValue) return ((RawValue)value).Text;
            throw new ArgumentException("지원하지 않는 값 형식: " + value.GetType().Name);
        }

        public static string EncodeDouble(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("유효하지 않은 숫자");
            string s = d.ToString("R", Inv);
            // Scheme 해석기가 정수로 오인하지 않도록 소수점 보장 (예: 70 -> 70.0)
            if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0 && s.IndexOf('e') < 0) s += ".0";
            return s.Replace("E", "e");
        }

        public static string EncodeString(string s)
        {
            if (s == null) s = "";
            if (s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                throw new ArgumentException("문자열 값에 줄바꿈을 넣을 수 없습니다.");
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        // ---------- 디코딩 ----------
        public static bool IsError(string raw)
        {
            return raw != null && raw.TrimStart().StartsWith("Error", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryBool(string raw, out bool v)
        {
            v = false;
            if (raw == null) return false;
            string t = raw.Trim();
            if (t == "#t" || t == "#T") { v = true; return true; }
            if (t == "#f" || t == "#F") { v = false; return true; }
            return false;
        }

        public static bool TryDouble(string raw, out double v)
        {
            v = double.NaN;
            if (raw == null) return false;
            return double.TryParse(raw.Trim(), NumberStyles.Float, Inv, out v) && double.IsFinite(v);
        }

        public static bool TryInt(string raw, out int v)
        {
            v = 0;
            if (raw == null) return false;
            string t = raw.Trim();
            if (int.TryParse(t, NumberStyles.Integer, Inv, out v)) return true;
            double d;
            if (double.TryParse(t, NumberStyles.Float, Inv, out d) && double.IsFinite(d)
                && d == Math.Truncate(d) && d >= int.MinValue && d <= int.MaxValue)
            { v = (int)d; return true; }
            return false;
        }

        /// <summary>문자열 값에서 따옴표를 제거 (따옴표가 없으면 원문 반환).</summary>
        public static string Str(string raw)
        {
            if (raw == null) return null;
            string t = raw.Trim();
            if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"')
            {
                t = t.Substring(1, t.Length - 2);
                return t.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return t;
        }

        /// <summary>바이너리 값(&amp;BASE64 또는 "BASE64")을 디코딩.</summary>
        public static byte[] Binary(string raw)
        {
            if (raw == null) return null;
            string t = raw.Trim();
            if (t.Length == 0) return new byte[0];
            if (t[0] == '&') t = t.Substring(1);
            else if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"') t = t.Substring(1, t.Length - 2);
            else if (t[0] == '"') throw new DecofException("바이너리 값 형식 오류");
            t = t.Replace("\r", "").Replace("\n", "").Trim();
            if (t.Length == 0) return new byte[0];
            try { return Convert.FromBase64String(t); }
            catch (FormatException ex) { throw new DecofException("BASE64 디코딩 실패", ex); }
        }

        /// <summary>튜플 "(1.0 2.0)" 또는 "'(1.0 2.0)" 을 문자열 토큰 목록으로 분해 (1단계).</summary>
        public static List<string> Tuple(string raw)
        {
            List<string> list = new List<string>();
            if (raw == null) return list;
            string t = raw.Trim();
            if (t.StartsWith("'")) t = t.Substring(1);
            if (t.StartsWith("(") && t.EndsWith(")")) t = t.Substring(1, t.Length - 2);
            StringBuilder cur = new StringBuilder();
            bool inStr = false; int depth = 0;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (inStr)
                {
                    cur.Append(c);
                    if (c == '\\' && i + 1 < t.Length) { cur.Append(t[++i]); continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; cur.Append(c); continue; }
                if (c == '(') depth++;
                if (c == ')') depth--;
                if (char.IsWhiteSpace(c) && depth == 0)
                {
                    if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); }
                    continue;
                }
                cur.Append(c);
            }
            if (cur.Length > 0) list.Add(cur.ToString());
            return list;
        }

        /// <summary>값을 사람이 읽기 좋은 문자열로 (따옴표 제거, 불리언 → ON/OFF 아님: 원형 유지).</summary>
        public static string Display(string raw)
        {
            if (raw == null) return "";
            return Str(raw);
        }
    }

    /// <summary>이미 인코딩된 Scheme 표현을 그대로 전달하기 위한 래퍼 (예: 'symbol, 튜플).</summary>
    public sealed class RawValue
    {
        public string Text { get; private set; }
        public RawValue(string text) { Text = text; }
        public override string ToString() { return Text; }
    }
}
