
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// CSV(가챠확률 + 주사위면가중치) 기반 주사위.
/// 1) DiceTables.RollDiceSingle()/RollDiceTen()으로 획득
/// 2) dice.Roll()로 1~6 결과를 굴림
/// </summary>

public enum DiceGrade { Common, Rare, Epic, Legendary }

// 뽑기 종류(1뽑 / 10뽑(1~9) / 10뽑(10번째 확정슬롯))
public enum PullType { Single, Ten_1to9, Ten_Slot10_Guarantee }
[Serializable]
#region Runtime Dice (굴리는 주사위)
public class Dice
{
    public DiceGrade grade;
    [Range(1, 5)] public int star;
    [Range(1, 10)] public int level;

    // CSV(dice_prob_table_grade_star_level)에서 읽어온 면 가중치
    // index 0..5 => face 1..6
    public int[] faceWeights = new int[6];

    public Dice(DiceGrade g, int s, int lv, int[] weights)
    {
        grade = g;
        star = Mathf.Clamp(s, 1, 5);
        level = Mathf.Clamp(lv, 1, 10);

        faceWeights = new int[6];
        if (weights != null && weights.Length >= 6)
            Array.Copy(weights, faceWeights, 6);
        else
            for (int i = 0; i < 6; i++) faceWeights[i] = 1; // fallback
    }

    /// <summary>가중치 기반으로 1~6을 뽑는다.</summary>
    public int Roll()
    {
        int total = 0;
        for (int i = 0; i < 6; i++) total += Mathf.Max(0, faceWeights[i]);
        if (total <= 0) return 1;

        int r = UnityEngine.Random.Range(0, total); // 0..total-1
        int acc = 0;
        for (int i = 0; i < 6; i++)
        {
            acc += Mathf.Max(0, faceWeights[i]);
            if (r < acc) return i + 1;
        }
        return 6;
    }
}
#endregion

/// <summary>
/// CSV를 로드해서:
/// - 가챠 확률표(dice_gacha_prob_table.csv)로 등급/별/레벨 추첨
/// - 주사위면 가중치표(dice_prob_table_grade_star_level.csv)로 1~6 가중치 세팅
/// </summary>
public static class DiceTables
{
    //Csv 경로(확장자 제외)
    private const string GACHA_CSV_FILE = "dice_gacha_prob_table.csv";
    private const string FACE_CSV_FILE = "dice_prob_table_grade_star_level.csv";
    private const string CSV_DIR = "Csv";

    // (grade, star, level) -> w1..w6
    private static readonly Dictionary<string, int[]> FaceWeightsByKey = new();

    // pullType -> 누적확률(CDF) 테이블
    private static readonly Dictionary<PullType, List<GachaEntry>> GachaByPullType = new();

    private static bool _loaded;
    public static bool IsLoaded => _loaded;

    [Serializable]
    private class GachaEntry
    {
        public DiceGrade grade;
        public int star;
        public int level;
        public float prob;
        public float cdf; // 누적
    }

    private static string MakeKey(DiceGrade g, int star, int level)
        => $"{g}|{star}|{level}";

    /// <summary>
    /// PC 전용 동기 로드: 게임 시작 시 1번만 호출하세요.
    /// </summary>
    public static void LoadOrThrow()
    {
        if (_loaded) return;

        string gachaPath = Path.Combine(Application.streamingAssetsPath, CSV_DIR, GACHA_CSV_FILE);
        string facePath = Path.Combine(Application.streamingAssetsPath, CSV_DIR, FACE_CSV_FILE);

        if (!File.Exists(gachaPath)) throw new Exception($"CSV not found: {gachaPath}");
        if (!File.Exists(facePath)) throw new Exception($"CSV not found: {facePath}");

        string gachaCsv = File.ReadAllText(gachaPath);
        string faceCsv = File.ReadAllText(facePath);

        LoadFaceWeights(faceCsv);
        LoadGachaTable(gachaCsv);

        _loaded = true;
    }

    // 1뽑: single 테이블 사용.
    public static Dice RollDiceSingle()
    {
        if (!_loaded) throw new Exception("DiceTables not loaded. Call DiceTables.LoadOrThrow() first.");
        return RollFrom(PullType.Single);
    }

    // 10뽑: 1~9는 single, 10번째는 Rare+ 확정 테이블 사용.
    public static List<Dice> RollDiceTen()
    {
        if (!_loaded) throw new Exception("DiceTables not loaded. Call DiceTables.LoadOrThrow() first.");

        var result = new List<Dice>(10);
        for (int i = 0; i < 9; i++) result.Add(RollFrom(PullType.Ten_1to9));
        result.Add(RollFrom(PullType.Ten_Slot10_Guarantee));
        return result;
    }


    // 저장된 OwnedDice → 런타임 Dice 복원(가중치는 CSV에서)
    public static Dice CreateDiceFromOwned(OwnedDice od)
    {
        if (!_loaded) throw new Exception("DiceTables not loaded. Call await LoadOrThrowAsync() first.");

        int s = Mathf.Clamp(od.Star, 1, 5);
        int lv = Mathf.Clamp(od.Level, 1, 10);
        var key = MakeKey(od.Grade, s, lv);

        if (!FaceWeightsByKey.TryGetValue(key, out var weights))
            weights = new[] { 1, 1, 1, 1, 1, 1 };

        return new Dice(od.Grade, s, lv, weights);
    }

    // 가챠 결과 Dice를 Account 인벤토리에 누적 저장
    public static void AddToAccountInventory(Account acc, Dice rolled)
    {
        // DiceTables.LoadOrThrow(); 는 이미 호출되어 있다고 가정
        // ExpTable도 로드되어 있어야 함
        DiceExpTable.LoadOrThrow();

        if (acc.DiceInventory == null) acc.DiceInventory = new List<OwnedDice>();

        // 중복 판단: Grade + Star 기준(레벨 무시)
        var found = acc.DiceInventory.Find(x => x.Grade == rolled.grade && x.Star == rolled.star);

        if (found != null) found.Count += 1;
        else
        {
            // 최초 획득: 가챠에서 나온 레벨로 시작(원하면 항상 1로 고정해도 됨)
            acc.DiceInventory.Add(new OwnedDice
            {
                Grade = rolled.grade,
                Star = rolled.star,
                Level = rolled.level,
                Count = 1,
                Exp = 0
            });
            return;
        }

        // “다음 레벨 필요 경험치의 20%” 지급
        int gain = DiceProgression.CalcDuplicateExp(found);
        DiceProgression.AddExpAndTryLevelUp(found, gain);
    }

    private static Dice RollFrom(PullType pullType)
    {
        if (!GachaByPullType.TryGetValue(pullType, out var list) || list.Count == 0)
            throw new Exception($"Gacha table empty for {pullType}");

        float r = UnityEngine.Random.value; // 0..1
        // 선형 탐색(200~400개면 충분). 더 크면 이진탐색으로 바꾸면 됨.
        for (int i = 0; i < list.Count; i++)
        {
            if (r <= list[i].cdf)
            {
                var e = list[i];
                var key = MakeKey(e.grade, e.star, e.level);
                if (!FaceWeightsByKey.TryGetValue(key, out var weights))
                {
                    // 혹시 키 누락되면 최소 주사위로 fallback
                    weights = new[] { 1, 1, 1, 1, 1, 1 };
                }
                return new Dice(e.grade, e.star, e.level, weights);
            }
        }

        // 마지막 fallback
        var last = list[list.Count - 1];
        var lastKey = MakeKey(last.grade, last.star, last.level);
        FaceWeightsByKey.TryGetValue(lastKey, out var wLast);
        return new Dice(last.grade, last.star, last.level, wLast);
    }

   
    // CSV Loaders
    // dice_prob_table_grade_star_level.csv
    // columns: grade,star,level,w1,w2,w3,w4,w5,w6, ... (p1..,c1..)
    private static void LoadFaceWeights(string csvText)
    {
        FaceWeightsByKey.Clear();

        var lines = SplitLines(csvText);
        if (lines.Count <= 1) throw new Exception("Face CSV has no data");

        var header = ParseCsvLine(lines[0]);
        int idxGrade = header.IndexOf("grade");
        int idxStar = header.IndexOf("star");
        int idxLevel = header.IndexOf("level");
        int idxW1 = header.IndexOf("w1");
        int idxW6 = header.IndexOf("w6");

        if (idxGrade < 0 || idxStar < 0 || idxLevel < 0 || idxW1 < 0 || idxW6 < 0)
            throw new Exception("Face CSV header missing required columns (grade/star/level/w1..w6)");

        for (int i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cols = ParseCsvLine(lines[i]);

            var g = (DiceGrade)Enum.Parse(typeof(DiceGrade), cols[idxGrade], true);
            int star = int.Parse(cols[idxStar]);
            int level = int.Parse(cols[idxLevel]);

            var w = new int[6];
            w[0] = int.Parse(cols[idxW1 + 0]);
            w[1] = int.Parse(cols[idxW1 + 1]);
            w[2] = int.Parse(cols[idxW1 + 2]);
            w[3] = int.Parse(cols[idxW1 + 3]);
            w[4] = int.Parse(cols[idxW1 + 4]);
            w[5] = int.Parse(cols[idxW1 + 5]);

            FaceWeightsByKey[MakeKey(g, star, level)] = w;
        }
    }

    // dice_gacha_prob_table.csv
    // columns: pull_type,grade,star,level,probability
    private static void LoadGachaTable(string csvText)
    {
        GachaByPullType.Clear();
        GachaByPullType[PullType.Single] = new List<GachaEntry>();
        GachaByPullType[PullType.Ten_1to9] = new List<GachaEntry>();
        GachaByPullType[PullType.Ten_Slot10_Guarantee] = new List<GachaEntry>();

        var lines = SplitLines(csvText);
        if (lines.Count <= 1) throw new Exception("Gacha CSV has no data");

        var header = ParseCsvLine(lines[0]);
        int idxPull = header.IndexOf("pull_type");
        int idxGrade = header.IndexOf("grade");
        int idxStar = header.IndexOf("star");
        int idxLevel = header.IndexOf("level");
        int idxProb = header.IndexOf("probability");

        if (idxPull < 0 || idxGrade < 0 || idxStar < 0 || idxLevel < 0 || idxProb < 0)
            throw new Exception("Gacha CSV header missing required columns");

        for (int i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cols = ParseCsvLine(lines[i]);

            PullType pullType = cols[idxPull] switch
            {
                "single" => PullType.Single,
                "ten_1to9" => PullType.Ten_1to9,
                "ten_slot10_guarantee" => PullType.Ten_Slot10_Guarantee,
                _ => PullType.Single
            };

            var entry = new GachaEntry
            {
                grade = (DiceGrade)Enum.Parse(typeof(DiceGrade), cols[idxGrade], true),
                star = int.Parse(cols[idxStar]),
                level = int.Parse(cols[idxLevel]),
                prob = float.Parse(cols[idxProb], System.Globalization.CultureInfo.InvariantCulture),
            };

            GachaByPullType[pullType].Add(entry);
        }

        // pullType별로 CDF 만들기(확률 합이 약간 오차 있을 수 있으니 1로 정규화)
        foreach (var kv in GachaByPullType)
        {
            var list = kv.Value;
            float sum = 0f;
            for (int i = 0; i < list.Count; i++) sum += Mathf.Max(0f, list[i].prob);
            if (sum <= 0f) throw new Exception($"Gacha prob sum is 0 for {kv.Key}");

            float acc = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                acc += Mathf.Max(0f, list[i].prob) / sum;
                list[i].cdf = acc;
            }
            // 마지막은 확실히 1로
            list[list.Count - 1].cdf = 1f;
        }
    }

    // CSV Utils (따옴표 포함 대응)
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        using var sr = new System.IO.StringReader(text);
        string line;
        while ((line = sr.ReadLine()) != null)
        {
            // BOM 제거
            if (lines.Count == 0) line = line.TrimStart('\uFEFF');
            lines.Add(line);
        }
        return lines;
    }

    // 간단 CSV 파서: "..." 내부의 콤마는 무시
    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        bool inQuotes = false;
        var cur = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                // "" -> "
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(cur.ToString());
                cur.Clear();
            }
            else
            {
                cur.Append(c);
            }
        }

        result.Add(cur.ToString());
        return result;
    }
}