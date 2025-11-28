using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

//주사위 레벨업 등급업에 따른 함수
public static class DiceExpTable
{
    private const string CSV_DIR = "Csv";
    private const string EXP_CSV_FILE = "dice_level_exp_table_round10.csv";

    // key: "Grade|Star|Level" (Level=1..9) -> expToNext
    private static readonly Dictionary<string, int> expToNextByKey = new();
    private static bool _loaded;

    private static string MakeKey(DiceGrade g, int star, int level) => $"{g}|{star}|{level}";

    public static void LoadOrThrow()
    {
        if (_loaded) return;

        string path = Path.Combine(Application.streamingAssetsPath, CSV_DIR, EXP_CSV_FILE);
        if (!File.Exists(path)) throw new Exception($"EXP CSV not found: {path}");

        string csv = File.ReadAllText(path);
        Parse(csv);

        _loaded = true;
    }

    public static int GetExpToNext(DiceGrade grade, int star, int level)
    {
        if (!_loaded) throw new Exception("DiceExpTable not loaded. Call DiceExpTable.LoadOrThrow() first.");

        if (level >= 10) return 0; // 만렙
        star = Mathf.Clamp(star, 1, 5);
        level = Mathf.Clamp(level, 1, 9);

        var key = MakeKey(grade, star, level);
        if (!expToNextByKey.TryGetValue(key, out var need))
            throw new Exception($"Missing expToNext in table: {key}");

        return need;
    }


    private static void Parse(string csvText)
    {
        expToNextByKey.Clear();

        var lines = SplitLines(csvText);
        if (lines.Count <= 1) throw new Exception("EXP CSV has no data");

        var header = ParseCsvLine(lines[0]);
        int idxGrade = header.IndexOf("grade");
        int idxStar = header.IndexOf("star");
        int idxLevel = header.IndexOf("level");
        int idxExp = header.IndexOf("expToNext");

        if (idxGrade < 0 || idxStar < 0 || idxLevel < 0 || idxExp < 0)
            throw new Exception("EXP CSV header missing required columns (grade,star,level,expToNext)");

        for (int i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var c = ParseCsvLine(lines[i]);

            var grade = (DiceGrade)Enum.Parse(typeof(DiceGrade), c[idxGrade], true);
            int star = int.Parse(c[idxStar]);
            int level = int.Parse(c[idxLevel]);
            int expToNext = int.Parse(c[idxExp]);

            expToNextByKey[MakeKey(grade, star, level)] = expToNext;
        }
    }

    // CSV utils
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        using var sr = new StringReader(text);
        string line;
        while ((line = sr.ReadLine()) != null)
        {
            if (lines.Count == 0) line = line.TrimStart('\uFEFF');
            lines.Add(line);
        }
        return lines;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        bool inQuotes = false;
        var cur = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
                    i++;
                }
                else inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(cur.ToString());
                cur.Clear();
            }
            else cur.Append(ch);
        }

        result.Add(cur.ToString());
        return result;
    }
}
