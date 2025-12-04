using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class LevelExpRow
{
    public int Level;
    public int NeedExp;
}

public class AccountLevelExpTable : MonoBehaviour
{
    public static AccountLevelExpTable Instance { get; private set; }

    [Header("StreamingAssets 하위 폴더/파일")]
    [SerializeField] private string folderName = "Csv";                
    [SerializeField] private string csvFileName = "BrumabulAccountLevelExp.csv";

    [Header("옵션")]
    [Tooltip("Awake/Start에서 자동으로 로드할지")]
    [SerializeField] private bool autoLoadOnStart = true;


    // 캐시
    public Dictionary<int, LevelExpRow> Table { get; private set; } = new Dictionary<int, LevelExpRow>();
    public int MaxLevelInTable { get; private set; } = 0;
    public bool IsLoaded { get; private set; } = false;

    // 로딩 완료 이벤트(원하면 구독해서 쓰기)
    public event Action<AccountLevelExpTable> OnLoaded;
    public event Action<string> OnLoadFailed;

    private void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (autoLoadOnStart)
            Load();
    }

    /// <summary>
    /// 외부에서 로드 시작
    /// </summary>
    public void Load()
    {
        // 중복 호출 방지(원하면 강제 재로드 함수 따로 만들기)
        if (IsLoaded)
        {
            Debug.Log("[AccountLevelExpTable] Already loaded.");
            OnLoaded?.Invoke(this);
            return;
        }

        StartCoroutine(LoadCoroutine());
    }

    /// <summary>
    /// StreamingAssets/CSV를 File IO로 읽어서 파싱
    /// (Android/WebGL 미지원 버전)
    /// </summary>
    private IEnumerator LoadCoroutine()
    {
        IsLoaded = false;
        Table.Clear();
        MaxLevelInTable = 0;

        // PC/Standalone에서는 streamingAssetsPath가 실제 폴더 경로
        string path = Path.Combine(Application.streamingAssetsPath, folderName, csvFileName);

        if (!File.Exists(path))
        {
            string msg = $"[AccountLevelExpTable] CSV not found: {path}\n" +
                         $"- StreamingAssets 폴더에 '{csvFileName}' 파일이 있는지 확인하세요.";
            Debug.LogError(msg);
            OnLoadFailed?.Invoke(msg);
            yield break;
        }

        string csvText;
        try
        {
            csvText = File.ReadAllText(path);
        }
        catch (Exception e)
        {
            string msg = $"[AccountLevelExpTable] CSV read failed: {path}\n{e}";
            Debug.LogError(msg);
            OnLoadFailed?.Invoke(msg);
            yield break;
        }

        // 파싱
        try
        {
            ParseCsv(csvText);
        }
        catch (Exception e)
        {
            string msg = $"[AccountLevelExpTable] CSV parse failed: {path}\n{e}";
            Debug.LogError(msg);
            OnLoadFailed?.Invoke(msg);
            yield break;
        }

        IsLoaded = true;
        Debug.Log($"[AccountLevelExpTable] Loaded OK. rows={Table.Count}, maxLevel={MaxLevelInTable}, path={path}");
        OnLoaded?.Invoke(this);

        yield return null;
    }

    /// <summary>
    /// CSV 형식:
    /// Level,NeedExp
    /// 1,100
    /// 2,120
    /// ...
    /// </summary>
    private void ParseCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            throw new Exception("CSV is empty.");

        using var sr = new StringReader(csv);

        string line;
        int lineNo = 0;
        bool headerSkipped = false;

        while ((line = sr.ReadLine()) != null)
        {
            lineNo++;

            line = line.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            // 헤더 1줄 스킵(유연 처리)
            if (!headerSkipped)
            {
                headerSkipped = true;
                // 헤더가 아니고 숫자로 시작하는 파일도 있을 수 있으니 검사:
                // 만약 첫 줄이 "1,100" 같은 데이터면 헤더로 스킵하지 않도록 되돌림
                // (첫 컬럼이 int로 파싱되면 데이터로 판단)
                var probe = line.Split(',');
                if (probe.Length >= 2 && int.TryParse(probe[0].Trim(), out _))
                {
                    // 첫 줄이 데이터네? -> 이 줄을 데이터로 처리하기 위해 headerSkipped만 true로 두고 아래 로직으로 진행
                }
                else
                {
                    // 진짜 헤더라면 다음 줄로
                    continue;
                }
            }

            // 콤마 분리
            var parts = line.Split(',');
            if (parts.Length < 2)
            {
                Debug.LogWarning($"[AccountLevelExpTable] Skip invalid line {lineNo}: '{line}'");
                continue;
            }

            if (!int.TryParse(parts[0].Trim(), out int level))
            {
                Debug.LogWarning($"[AccountLevelExpTable] Skip line {lineNo} (Level parse fail): '{line}'");
                continue;
            }

            if (!int.TryParse(parts[1].Trim(), out int needExp))
            {
                Debug.LogWarning($"[AccountLevelExpTable] Skip line {lineNo} (NeedExp parse fail): '{line}'");
                continue;
            }

            if (level <= 0)
            {
                Debug.LogWarning($"[AccountLevelExpTable] Skip line {lineNo} (Level <= 0): '{line}'");
                continue;
            }

            if (needExp < 0)
            {
                Debug.LogWarning($"[AccountLevelExpTable] Skip line {lineNo} (NeedExp < 0): '{line}'");
                continue;
            }

            // 중복 레벨 처리: 나중 값이 덮어씀(원하면 경고만 띄우고 continue 가능)
            if (Table.ContainsKey(level))
                Debug.LogWarning($"[AccountLevelExpTable] Duplicate level {level} at line {lineNo}. Overwrite.");

            Table[level] = new LevelExpRow { Level = level, NeedExp = needExp };
            if (level > MaxLevelInTable) MaxLevelInTable = level;
        }

        if (Table.Count == 0)
            throw new Exception("No valid rows parsed from CSV.");
    }

    /// <summary>
    /// 특정 레벨에서 다음 레벨까지 필요한 경험치 반환
    /// (예: level=1 -> NeedExp(1))
    /// </summary>
    public bool TryGetNeedExp(int level, out int needExp)
    {
        needExp = 0;
        if (!IsLoaded) return false;

        if (Table.TryGetValue(level, out var row))
        {
            needExp = row.NeedExp;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 필요하면 강제 재로드
    /// </summary>
    public void Reload()
    {
        IsLoaded = false;
        Table.Clear();
        MaxLevelInTable = 0;
        StartCoroutine(LoadCoroutine());
    }
}
