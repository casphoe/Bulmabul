using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class ProfanityFilter
{
    // 정책: true면 금지어 발견 시 메시지 자체 차단, false면 마스킹
    public static bool BlockMode = false;

    // ===== 금지어 리스트 =====
    // "ㅈ" 같은 1글자 초성은 오탐이 너무 심해서 제외(완벽해지려면 필수)
    // 대신 ㅈㄴ, ㅈ같 같은 “조합형”으로 커버
    private static readonly string[] Banned = new[]
    {
        // ===== 한국어(욕설/비속어) =====
        "씨발", "시발", "ㅅㅂ", "ㅆㅂ",
        "병신", "ㅂㅅ",
        "미친", "미친놈", "미친년",
        "개새끼", "개새", "새끼", "새꺄",
        "좆", "존나", "ㅈㄴ",
        "좆같", "ㅈ같", // “부분 문자열”로 잡아서 좆같다/ㅈ같다 변형 커버
        "엿", "지랄",
        "닥쳐", "꺼져", "꺼져라",

        // ===== 한국어(성적 비속어/음란) =====
        "보지", "자지",
        "꼬추", "후장",
        "섹스", "야동", "야사", "야설",

        // ===== 영어(욕설) =====
        "fuck", "fucker", "motherfucker",
        "shit", "bullshit",
        "bitch",
        "asshole",
        "bastard",
        "damn",

        // ===== 영어(성적 비속어/음란) =====
        "pussy",
        "dick", "cock",
        "cunt",
        "porn", "sex",

        // (주의) "ass"는 class/pass 같은 단어 오탐이 너무 많음 → 아래 SpecialShortWord로 처리
        // "ass",
    };

    // 짧은 영단어는 \b 경계가 없으면 오탐이 심해서 별도 처리
    private static readonly string[] SpecialShortWords = new[]
    {
        "ass",
        "sex" // sex도 'sextant' 같은 단어는 드물지만 그래도 경계 처리 권장
    };

    // ===== 캐시된 패턴 =====
    // 1) 정규화 문자열용(검출): Normalize() 결과에서 검사
    private static readonly List<Regex> _normPatterns = BuildNormalizedPatterns(Banned, SpecialShortWords);

    // 2) 원문 마스킹용: 공백/특수문자/숫자 끼워넣기 변형까지 원문에서 찾아 "***"로 치환
    private static readonly List<Regex> _rawMaskPatterns = BuildRawMaskPatterns(Banned, SpecialShortWords);

    /// <summary>
    /// 금지어 포함 여부(검출)
    /// </summary>
    public static bool HasBanned(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        string norm = Normalize(input);

        for (int i = 0; i < _normPatterns.Count; i++)
            if (_normPatterns[i].IsMatch(norm))
                return true;

        return false;
    }

    /// <summary>
    /// 필터 적용
    /// - BlockMode=true: 금지어 있으면 null 반환(전송 차단)
    /// - BlockMode=false: 원문에서 금지어 부분을 "***"로 마스킹해서 반환
    /// </summary>
    public static string Filter(string input, out bool blockedOrChanged)
    {
        blockedOrChanged = false;
        if (string.IsNullOrWhiteSpace(input)) return input;

        string norm = Normalize(input);

        bool hit = false;
        for (int i = 0; i < _normPatterns.Count; i++)
        {
            if (_normPatterns[i].IsMatch(norm))
            {
                hit = true;
                break;
            }
        }

        if (!hit) return input;

        blockedOrChanged = true;

        if (BlockMode)
            return null; // 차단

        // 마스킹(원문 기준): 공백/특수문자/숫자 섞어서 우회한 것도 최대한 "***"로 치환
        string masked = input;

        // 긴 단어부터 먼저 마스킹(짧은 단어가 먼저 먹으면 범위가 깨질 수 있음)
        for (int i = 0; i < _rawMaskPatterns.Count; i++)
        {
            masked = _rawMaskPatterns[i].Replace(masked, "***");
        }

        return masked;
    }

    // ---------------- 내부 ----------------

    /// <summary>
    /// Normalize:
    /// 1) 소문자화
    /// 2) 공백/구두점/심볼 제거 (ㅅ ㅂ / s.h.i.t 같은 우회 제거)
    /// 3) 숫자 제거 (f1u2c3k / ㅅ1ㅂ 우회 제거)  ✅ 여기 추가
    /// 4) 반복 문자 축약(3회 이상 -> 2회)
    /// </summary>
    private static string Normalize(string s)
    {
        s = s.ToLowerInvariant();

        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (char.IsPunctuation(c)) continue;
            if (char.IsSymbol(c)) continue;

            // 숫자 끼워넣기 우회 제거
            if (char.IsDigit(c)) continue;

            sb.Append(c);
        }

        s = sb.ToString();

        // 같은 문자를 3번 이상 반복하면 2번으로 축약
        s = Regex.Replace(s, @"(.)\1{2,}", "$1$1");

        return s;
    }

    /// <summary>
    /// 정규화 문자열용 패턴:
    /// - Normalize() 후 검사하므로 공백/특수/숫자 우회는 대부분 제거됨
    /// - 그래도 “짧은 영단어”는 경계 처리(오탐 방지)
    /// </summary>
    private static List<Regex> BuildNormalizedPatterns(string[] banned, string[] shortWords)
    {
        var list = new List<(string key, Regex rx)>();

        // 일반 금지어
        foreach (var w in banned)
        {
            if (string.IsNullOrWhiteSpace(w)) continue;

            string ww = w.ToLowerInvariant();
            string escaped = Regex.Escape(ww);

            // Normalize에서는 이미 특수/공백/숫자 제거됨 → 그냥 포함 검사면 강함
            list.Add((ww, new Regex(escaped, RegexOptions.Compiled)));
        }

        // 짧은 영단어는 단어 경계(letters/digits)로 감싸서 오탐 줄이기
        foreach (var w in shortWords)
        {
            if (string.IsNullOrWhiteSpace(w)) continue;

            string ww = w.ToLowerInvariant();
            string escaped = Regex.Escape(ww);

            // \b는 유니코드/언더스코어 등에서 애매할 수 있어서 직접 경계 체크
            // 앞/뒤가 영숫자가 아니어야 함
            string pattern = $@"(?<![a-z0-9]){escaped}(?![a-z0-9])";
            list.Add((ww, new Regex(pattern, RegexOptions.Compiled)));
        }

        // 긴 패턴부터 먼저 검사하면(선택) 약간 효율/정확도 도움
        return list
            .OrderByDescending(x => x.key.Length)
            .Select(x => x.rx)
            .ToList();
    }

    /// <summary>
    /// 원문 마스킹용 패턴:
    /// - "s.h.i.t", "ㅅ ㅂ", "f1u2c3k" 같은 우회를 원문에서 직접 잡아서 "***"로 치환
    /// - 각 글자 사이에 [공백/구두점/심볼/언더스코어/숫자]가 얼마든지 끼어들 수 있게 허용
    /// - 짧은 영단어는 앞/뒤 경계도 함께 처리
    /// </summary>
    private static List<Regex> BuildRawMaskPatterns(string[] banned, string[] shortWords)
    {
        var list = new List<(string key, Regex rx)>();

        // “사이 끼움 허용” 클래스
        // - 공백(\s), 구두점(\p{P}), 심볼(\p{S}), 언더스코어, 숫자
        const string GAP = @"[\s\p{P}\p{S}_0-9]*";

        void AddWord(string word, bool enforceBoundary)
        {
            string w = (word ?? "").Trim();
            if (w.Length == 0) return;

            // 글자 단위로 쪼개서 사이사이에 GAP를 끼워서 패턴 구성
            // 예) "shit" -> s[GAP]h[GAP]i[GAP]t
            var chars = w.ToCharArray();
            var sb = new StringBuilder();

            for (int i = 0; i < chars.Length; i++)
            {
                string ch = Regex.Escape(chars[i].ToString());
                sb.Append(ch);
                if (i != chars.Length - 1) sb.Append(GAP);
            }

            string core = sb.ToString();

            // 경계 강제(짧은 영단어 오탐 방지)
            // - 앞/뒤가 영숫자면 안됨
            string pattern = enforceBoundary
                ? $@"(?<![A-Za-z0-9]){core}(?![A-Za-z0-9])"
                : core;

            list.Add((w, new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase)));
        }

        // 일반 금지어
        foreach (var w in banned)
        {
            if (string.IsNullOrWhiteSpace(w)) continue;

            bool enforceBoundary = IsAsciiWord(w) && w.Length <= 3; // 짧은 영단어만 경계
            AddWord(w, enforceBoundary);
        }

        // special short words는 항상 경계
        foreach (var w in shortWords)
        {
            if (string.IsNullOrWhiteSpace(w)) continue;
            AddWord(w, enforceBoundary: true);
        }

        // 긴 단어 먼저 마스킹
        return list
            .OrderByDescending(x => x.key.Length)
            .Select(x => x.rx)
            .ToList();
    }

    private static bool IsAsciiWord(string w)
    {
        foreach (char c in w)
        {
            if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z'))
                return false;
        }
        return true;
    }
}
