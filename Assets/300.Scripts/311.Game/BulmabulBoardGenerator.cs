using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 시작 시 부루마불 형태의 사각형 보드판을 자동 생성한다.
/// 
/// 기본 구조:
/// - 시작 위치: (0, 0, -10)
/// - 총 160칸
/// - 한 변 40칸
/// - 사각형 둘레를 따라 배치
/// - 생성된 BulmabulCellData를 BulmabulBoard에 자동 등록
/// </summary>
public class BulmabulBoardGenerator : MonoBehaviour
{
    [Serializable]
    public class SpecialCellSetting
    {
        [Tooltip("특수 칸 인덱스. 0~159")]
        public int cellIndex;

        [Tooltip("해당 칸 타입")]
        public BulmabulCellType cellType = BulmabulCellType.Land;

        [Tooltip("해당 타입 전용 프리팹. 비워두면 타입별 기본 프리팹 사용")]
        public GameObject overridePrefab;

        [Header("Optional Data Override")]
        public string customName;

        public int buyCost;
        public int tollCost;

        public int smallHouseBuildCost;
        public int houseBuildCost;
        public int bigHouseBuildCost;
        public int hotelBuildCost;

        public int smallHouseToll;
        public int houseToll;
        public int bigHouseToll;
        public int hotelToll;

        public int taxCost;
        [Header("Bonus Random Amount")]
        public int bonusMinAmount;
        public int bonusMaxAmount;
        public int travelCost;

        public int lineId = -1;
        public bool isLandmark;
    }

    #region 각 땅마다 가격대 차이 기능
    [Serializable]
    public class LandPriceTier
    {
        [Header("적용 범위")]
        public int startIndex;
        public int endIndex;

        [Header("땅 가격")]
        public int buyCost;
        public int tollCost;

        [Header("건물 건설 비용")]
        public int smallHouseBuildCost;
        public int houseBuildCost;
        public int bigHouseBuildCost;

        [Tooltip("호텔 건설 비용. 호텔은 최종 랜드마크 역할")]
        public int hotelBuildCost;

        [Header("건물 추가 통행료")]
        public int smallHouseToll;
        public int houseToll;
        public int bigHouseToll;

        [Tooltip("호텔 추가 통행료. 호텔은 최종 랜드마크 역할")]
        public int hotelToll;

        [Header("라인 / 독점")]
        public int lineId;
    }


    [Header("Land Price Tiers")]
    [SerializeField]
    private LandPriceTier[] landPriceTiers =
 {
    new LandPriceTier
    {
        startIndex = 1,
        endIndex = 39,
        buyCost = 100000,
        tollCost = 20000,

        smallHouseBuildCost = 30000,
        houseBuildCost = 50000,
        bigHouseBuildCost = 80000,
        hotelBuildCost = 200000,

        smallHouseToll = 10000,
        houseToll = 30000,
        bigHouseToll = 60000,
        hotelToll = 220000,

        lineId = 0
    },

    new LandPriceTier
    {
        startIndex = 41,
        endIndex = 79,
        buyCost = 180000,
        tollCost = 35000,

        smallHouseBuildCost = 50000,
        houseBuildCost = 80000,
        bigHouseBuildCost = 120000,
        hotelBuildCost = 300000,

        smallHouseToll = 20000,
        houseToll = 50000,
        bigHouseToll = 90000,
        hotelToll = 350000,

        lineId = 1
    },

    new LandPriceTier
    {
        startIndex = 81,
        endIndex = 119,
        buyCost = 300000,
        tollCost = 60000,

        smallHouseBuildCost = 80000,
        houseBuildCost = 120000,
        bigHouseBuildCost = 180000,
        hotelBuildCost = 450000,

        smallHouseToll = 40000,
        houseToll = 90000,
        bigHouseToll = 160000,
        hotelToll = 550000,

        lineId = 2
    },

    new LandPriceTier
    {
        startIndex = 121,
        endIndex = 159,
        buyCost = 500000,
        tollCost = 100000,

        smallHouseBuildCost = 120000,
        houseBuildCost = 180000,
        bigHouseBuildCost = 260000,
        hotelBuildCost = 700000,

        smallHouseToll = 70000,
        houseToll = 150000,
        bigHouseToll = 280000,
        hotelToll = 900000,

        lineId = 3
    }
};

    private LandPriceTier GetPriceTier(int cellIndex)
    {
        if (landPriceTiers == null)
            return null;

        for (int i = 0; i < landPriceTiers.Length; i++)
        {
            LandPriceTier tier = landPriceTiers[i];

            if (tier == null)
                continue;

            if (cellIndex >= tier.startIndex && cellIndex <= tier.endIndex)
                return tier;
        }

        return null;
    }
    #endregion

    #region 땅 이름

    #region 한국 맵 관광지 이름
    [Header("Korea Land Names - Korean")]
    [SerializeField]
    private string[] koreaLandNamesKor =
{
    "경복궁", "창덕궁", "덕수궁", "광화문", "청계천",
    "남산서울타워", "명동", "홍대거리", "이태원", "북촌한옥마을",
    "인사동", "한강공원", "롯데월드", "코엑스", "동대문디자인플라자",

    "수원화성", "한국민속촌", "에버랜드", "서울랜드", "파주헤이리마을",
    "임진각", "남이섬", "아침고요수목원", "쁘띠프랑스", "제이드가든",
    "가평자라섬", "양평두물머리", "포천아트밸리", "광명동굴", "대부도",

    "인천차이나타운", "월미도", "송도센트럴파크", "소래포구", "강화도",
    "전등사", "석모도", "을왕리해수욕장", "무의도", "영종도",

    "춘천소양강", "강릉경포대", "강릉안목해변", "정동진", "속초해수욕장",
    "설악산", "낙산사", "오죽헌", "주문진", "대관령양떼목장",
    "평창알펜시아", "월정사", "정선아리랑시장", "삼척해상케이블카", "환선굴",

    "대전엑스포과학공원", "계룡산", "공주공산성", "공주무령왕릉", "부여궁남지",
    "부소산성", "천안독립기념관", "아산외암마을", "태안꽃지해수욕장", "안면도",
    "보령대천해수욕장", "청주청남대", "청주상당산성", "단양도담삼봉", "단양고수동굴",

    "전주한옥마을", "전주경기전", "군산근대거리", "선유도", "익산미륵사지",
    "남원광한루원", "고창읍성", "고창선운사", "변산반도", "내장산",
    "담양죽녹원", "담양메타세쿼이아길", "순천만국가정원", "순천만습지", "여수밤바다",
    "여수오동도", "향일암", "목포근대역사거리", "보성녹차밭", "강진다산초당",

    "광주국립아시아문화전당", "무등산", "양림동역사문화마을", "송정역시장", "충장로",

    "부산해운대", "광안리", "태종대", "감천문화마을", "자갈치시장",
    "부산국제시장", "송도해상케이블카", "오륙도", "동백섬", "해동용궁사",
    "다대포해수욕장", "기장죽성성당", "부산시민공원", "영도흰여울문화마을", "범어사",

    "경주불국사", "석굴암", "첨성대", "동궁과월지", "대릉원",
    "황리단길", "포항호미곶", "영덕블루로드", "안동하회마을", "도산서원",
    "문경새재", "상주경천대", "울진성류굴", "봉화분천역", "청송주산지",

    "대구서문시장", "김광석거리", "팔공산", "앞산전망대", "이월드",
    "울산대왕암공원", "태화강국가정원", "간절곶", "장생포고래문화마을", "울산바위",

    "창원진해군항제", "통영동피랑마을", "통영케이블카", "거제바람의언덕", "외도보타니아",
    "남해독일마을", "사천바다케이블카", "진주성", "합천해인사", "밀양영남루",
    "김해가야테마파크", "창녕우포늪", "하동쌍계사", "함양상림공원", "거창수승대",

    "제주성산일출봉", "한라산", "우도", "협재해수욕장", "함덕해수욕장",
    "섭지코지", "천지연폭포", "정방폭포", "용두암", "카멜리아힐",
    "오설록티뮤지엄", "비자림", "만장굴", "새별오름", "산방산",
    "중문관광단지", "주상절리대", "마라도", "가파도", "제주돌문화공원"
};

    [Header("Korea Land Names - English")]
    [SerializeField]
    private string[] koreaLandNamesEng =
    {
    "Gyeongbokgung Palace", "Changdeokgung Palace", "Deoksugung Palace", "Gwanghwamun", "Cheonggyecheon Stream",
    "N Seoul Tower", "Myeongdong", "Hongdae Street", "Itaewon", "Bukchon Hanok Village",
    "Insadong", "Hangang Park", "Lotte World", "COEX", "Dongdaemun Design Plaza",

    "Suwon Hwaseong Fortress", "Korean Folk Village", "Everland", "Seoul Land", "Paju Heyri Art Village",
    "Imjingak", "Nami Island", "Garden of Morning Calm", "Petite France", "Jade Garden",
    "Gapyeong Jaraseom Island", "Yangpyeong Dumulmeori", "Pocheon Art Valley", "Gwangmyeong Cave", "Daebudo Island",

    "Incheon Chinatown", "Wolmido Island", "Songdo Central Park", "Sorae Port", "Ganghwa Island",
    "Jeondeungsa Temple", "Seokmodo Island", "Eurwangni Beach", "Muuido Island", "Yeongjongdo Island",

    "Chuncheon Soyang River", "Gangneung Gyeongpodae", "Gangneung Anmok Beach", "Jeongdongjin", "Sokcho Beach",
    "Seoraksan Mountain", "Naksansa Temple", "Ojukheon", "Jumunjin", "Daegwallyeong Sheep Ranch",
    "Pyeongchang Alpensia", "Woljeongsa Temple", "Jeongseon Arirang Market", "Samcheok Marine Cable Car", "Hwanseon Cave",

    "Daejeon Expo Science Park", "Gyeryongsan Mountain", "Gongju Gongsanseong Fortress", "Tomb of King Muryeong", "Buyeo Gungnamji Pond",
    "Busosanseong Fortress", "Independence Hall of Korea", "Asan Oeam Folk Village", "Taean Kkotji Beach", "Anmyeondo Island",
    "Boryeong Daecheon Beach", "Cheongju Cheongnamdae", "Cheongju Sangdangsanseong Fortress", "Danyang Dodamsambong Peaks", "Danyang Gosu Cave",

    "Jeonju Hanok Village", "Jeonju Gyeonggijeon Shrine", "Gunsan Modern History Street", "Seonyudo Island", "Iksan Mireuksaji Site",
    "Namwon Gwanghalluwon Garden", "Gochang Eupseong Fortress", "Gochang Seonunsa Temple", "Byeonsanbando National Park", "Naejangsan Mountain",
    "Damyang Juknokwon Bamboo Garden", "Damyang Metasequoia Road", "Suncheonman National Garden", "Suncheon Bay Wetland", "Yeosu Night Sea",
    "Yeosu Odongdo Island", "Hyangiram Hermitage", "Mokpo Modern History Street", "Boseong Green Tea Field", "Gangjin Dasan Chodang",

    "Gwangju Asia Culture Center", "Mudeungsan Mountain", "Yangnim-dong History Village", "Songjeong Station Market", "Chungjang-ro",

    "Busan Haeundae Beach", "Gwangalli Beach", "Taejongdae", "Gamcheon Culture Village", "Jagalchi Market",
    "Busan International Market", "Songdo Marine Cable Car", "Oryukdo Island", "Dongbaekseom Island", "Haedong Yonggungsa Temple",
    "Dadaepo Beach", "Gijang Jukseong Church", "Busan Citizens Park", "Yeongdo Huinnyeoul Culture Village", "Beomeosa Temple",

    "Gyeongju Bulguksa Temple", "Seokguram Grotto", "Cheomseongdae Observatory", "Donggung Palace and Wolji Pond", "Daereungwon Tomb Complex",
    "Hwangnidan-gil", "Pohang Homigot", "Yeongdeok Blue Road", "Andong Hahoe Village", "Dosan Seowon",
    "Mungyeong Saejae", "Sangju Gyeongcheondae", "Uljin Seongnyugul Cave", "Bonghwa Buncheon Station", "Cheongsong Jusanji Pond",

    "Daegu Seomun Market", "Kim Gwangseok Street", "Palgongsan Mountain", "Apsan Observatory", "E-World",
    "Ulsan Daewangam Park", "Taehwagang National Garden", "Ganjeolgot Cape", "Jangsaengpo Whale Culture Village", "Ulsanbawi Rock",

    "Changwon Jinhae Gunhangje Festival", "Tongyeong Dongpirang Village", "Tongyeong Cable Car", "Geoje Windy Hill", "Oedo Botania",
    "Namhae German Village", "Sacheon Ocean Cable Car", "Jinjuseong Fortress", "Hapcheon Haeinsa Temple", "Miryang Yeongnamnu Pavilion",
    "Gimhae Gaya Theme Park", "Changnyeong Upo Wetland", "Hadong Ssanggyesa Temple", "Hamyang Sangnim Park", "Geochang Suseungdae",

    "Jeju Seongsan Ilchulbong", "Hallasan Mountain", "Udo Island", "Hyeopjae Beach", "Hamdeok Beach",
    "Seopjikoji", "Cheonjiyeon Falls", "Jeongbang Falls", "Yongduam Rock", "Camellia Hill",
    "Osulloc Tea Museum", "Bijarim Forest", "Manjanggul Cave", "Saebyeol Oreum", "Sanbangsan Mountain",
    "Jungmun Tourist Complex", "Jusangjeolli Cliff", "Marado Island", "Gapado Island", "Jeju Stone Park"
    };
    #endregion

    #region 미국 관광지

    [Header("USA Land Names - Korean")]
    [SerializeField]
    private string[] usaLandNamesKor =
{
    "자유의 여신상", "타임스 스퀘어", "센트럴 파크", "엠파이어 스테이트 빌딩", "브루클린 브리지",
    "메트로폴리탄 미술관", "브로드웨이", "록펠러 센터", "그랜드 센트럴 터미널", "월스트리트",
    "원 월드 트레이드 센터", "나이아가라 폭포", "보스턴 프리덤 트레일", "하버드 대학교", "펜웨이 파크",

    "내셔널 몰", "백악관", "미국 국회의사당", "링컨 기념관", "스미소니언 박물관",
    "워싱턴 기념탑", "마운트 버논", "독립기념관", "자유의 종", "필라델피아 미술관",
    "애틀랜틱시티 보드워크", "케이프 메이", "볼티모어 이너 하버", "포트 맥헨리", "애나폴리스 역사 지구",

    "버지니아 비치", "콜로니얼 윌리엄스버그", "제임스타운 정착지", "셰넌도어 국립공원", "블루리지 파크웨이",
    "그레이트 스모키 산맥", "빌트모어 에스테이트", "아우터 뱅크스", "머틀 비치", "찰스턴 역사 지구",
    "서배너 역사 지구", "스톤 마운틴", "조지아 아쿠아리움", "월드 오브 코카콜라", "애틀랜타 벨트라인",

    "월트 디즈니 월드", "유니버설 올랜도", "마이애미 비치", "에버글레이즈 국립공원", "키웨스트",
    "케네디 우주센터", "탬파 리버워크", "새니벨 섬", "뉴올리언스 프렌치 쿼터", "잭슨 스퀘어",
    "버번 스트리트", "오크 앨리 플랜테이션", "그레이스랜드", "빌 스트리트", "내슈빌 브로드웨이",

    "그랜드 올 오프리", "컨트리 음악 명예의 전당", "매머드 케이브 국립공원", "게이트웨이 아치", "시카고 밀레니엄 파크",
    "클라우드 게이트", "네이비 피어", "윌리스 타워 스카이덱", "시카고 미술관", "매그니피센트 마일",
    "인디애나 듄스 국립공원", "헨리 포드 박물관", "매키낙 섬", "몰 오브 아메리카", "러시모어 산",

    "배드랜즈 국립공원", "시어도어 루스벨트 국립공원", "옐로스톤 국립공원", "그랜드 티턴 국립공원", "글레이셔 국립공원",
    "로키산 국립공원", "덴버 유니언 스테이션", "가든 오브 더 갓스", "메사 버드 국립공원", "아치스 국립공원",
    "캐니언랜즈 국립공원", "브라이스 캐니언 국립공원", "자이언 국립공원", "모뉴먼트 밸리", "앤털로프 캐니언",

    "호스슈 벤드", "그랜드 캐니언", "세도나", "사와로 국립공원", "라스베이거스 스트립",
    "벨라지오 분수", "후버 댐", "데스밸리 국립공원", "요세미티 국립공원", "세쿼이아 국립공원",
    "킹스 캐니언 국립공원", "금문교", "피셔맨스 워프", "앨커트래즈 섬", "롬바드 스트리트",

    "실리콘 밸리", "산타모니카 피어", "할리우드 사인", "할리우드 명예의 거리", "그리피스 천문대",
    "유니버설 스튜디오 할리우드", "디즈니랜드 파크", "라구나 비치", "샌디에이고 동물원", "발보아 파크",
    "코로나도 비치", "조슈아트리 국립공원", "팜스프링스", "빅서", "몬터레이 베이 아쿠아리움",

    "17마일 드라이브", "허스트 캐슬", "나파 밸리", "레이크 타호", "크레이터 레이크 국립공원",
    "포틀랜드 일본 정원", "캐논 비치", "마운트 레이니어 국립공원", "스페이스 니들", "파이크 플레이스 마켓",
    "올림픽 국립공원", "노스 캐스케이드 국립공원", "마운트 세인트 헬렌스", "레번워스", "보이시 리버 그린벨트",

    "선 밸리", "크레이터스 오브 더 문", "솔트레이크 템플 스퀘어", "파크 시티", "그레이트 솔트 레이크",
    "산타페 플라자", "칼즈배드 동굴", "화이트 샌즈 국립공원", "앨버커키 올드타운", "타오스 푸에블로",
    "샌안토니오 리버워크", "알라모", "휴스턴 우주센터", "댈러스 리유니언 타워", "포트워스 스톡야즈",

    "오스틴 주 의사당", "사우스 파드레 아일랜드", "오클라호마시티 국립기념관", "루트 66 박물관", "캔자스시티 유니언 스테이션",
    "할레아칼라 국립공원", "와이키키 비치", "진주만", "나팔리 코스트", "하와이 화산 국립공원"
};

    [Header("USA Land Names - English")]
    [SerializeField]
    private string[] usaLandNamesEng =
    {
    "Statue of Liberty", "Times Square", "Central Park", "Empire State Building", "Brooklyn Bridge",
    "Metropolitan Museum of Art", "Broadway", "Rockefeller Center", "Grand Central Terminal", "Wall Street",
    "One World Trade Center", "Niagara Falls", "Boston Freedom Trail", "Harvard University", "Fenway Park",

    "National Mall", "White House", "U.S. Capitol", "Lincoln Memorial", "Smithsonian Museums",
    "Washington Monument", "Mount Vernon", "Independence Hall", "Liberty Bell", "Philadelphia Museum of Art",
    "Atlantic City Boardwalk", "Cape May", "Inner Harbor", "Fort McHenry", "Annapolis Historic District",

    "Virginia Beach", "Colonial Williamsburg", "Jamestown Settlement", "Shenandoah National Park", "Blue Ridge Parkway",
    "Great Smoky Mountains", "Biltmore Estate", "Outer Banks", "Myrtle Beach", "Charleston Historic District",
    "Savannah Historic District", "Stone Mountain", "Georgia Aquarium", "World of Coca-Cola", "Atlanta BeltLine",

    "Walt Disney World", "Universal Orlando", "Miami Beach", "Everglades National Park", "Key West",
    "Kennedy Space Center", "Tampa Riverwalk", "Sanibel Island", "New Orleans French Quarter", "Jackson Square",
    "Bourbon Street", "Oak Alley Plantation", "Graceland", "Beale Street", "Nashville Broadway",

    "Grand Ole Opry", "Country Music Hall of Fame", "Mammoth Cave National Park", "Gateway Arch", "Chicago Millennium Park",
    "Cloud Gate", "Navy Pier", "Willis Tower Skydeck", "Art Institute of Chicago", "Magnificent Mile",
    "Indiana Dunes National Park", "Henry Ford Museum", "Mackinac Island", "Mall of America", "Mount Rushmore",

    "Badlands National Park", "Theodore Roosevelt National Park", "Yellowstone National Park", "Grand Teton National Park", "Glacier National Park",
    "Rocky Mountain National Park", "Denver Union Station", "Garden of the Gods", "Mesa Verde National Park", "Arches National Park",
    "Canyonlands National Park", "Bryce Canyon National Park", "Zion National Park", "Monument Valley", "Antelope Canyon",

    "Horseshoe Bend", "Grand Canyon", "Sedona", "Saguaro National Park", "Las Vegas Strip",
    "Bellagio Fountains", "Hoover Dam", "Death Valley National Park", "Yosemite National Park", "Sequoia National Park",
    "Kings Canyon National Park", "Golden Gate Bridge", "Fisherman's Wharf", "Alcatraz Island", "Lombard Street",

    "Silicon Valley", "Santa Monica Pier", "Hollywood Sign", "Hollywood Walk of Fame", "Griffith Observatory",
    "Universal Studios Hollywood", "Disneyland Park", "Laguna Beach", "San Diego Zoo", "Balboa Park",
    "Coronado Beach", "Joshua Tree National Park", "Palm Springs", "Big Sur", "Monterey Bay Aquarium",

    "17-Mile Drive", "Hearst Castle", "Napa Valley", "Lake Tahoe", "Crater Lake National Park",
    "Portland Japanese Garden", "Cannon Beach", "Mount Rainier National Park", "Space Needle", "Pike Place Market",
    "Olympic National Park", "North Cascades National Park", "Mount St. Helens", "Leavenworth", "Boise River Greenbelt",

    "Sun Valley", "Craters of the Moon", "Salt Lake Temple Square", "Park City", "Great Salt Lake",
    "Santa Fe Plaza", "Carlsbad Caverns", "White Sands National Park", "Albuquerque Old Town", "Taos Pueblo",
    "San Antonio River Walk", "The Alamo", "Space Center Houston", "Dallas Reunion Tower", "Fort Worth Stockyards",

    "Austin State Capitol", "South Padre Island", "Oklahoma City National Memorial", "Route 66 Museum", "Kansas City Union Station",
    "Haleakala National Park", "Waikiki Beach", "Pearl Harbor", "Na Pali Coast", "Volcanoes National Park"
};

    #endregion

    #endregion

    [Header("Board")]
    [SerializeField] private BulmabulBoard board;

    [Header("Root")]
    [SerializeField] private Transform cellRoot;

    [Header("Prefabs")]
    [SerializeField] private GameObject landCellPrefab;
    [SerializeField] private GameObject startCellPrefab;
    [SerializeField] private GameObject taxCellPrefab;
    [SerializeField] private GameObject bonusCellPrefab;
    [SerializeField] private GameObject chanceCellPrefab;
    [SerializeField] private GameObject jailCellPrefab;
    [SerializeField] private GameObject travelCellPrefab;

    [Header("Generate Settings")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private int totalCellCount = 160;
    [SerializeField] private int cellsPerSide = 40;
    [Tooltip("시작 지점. 0번 발판 위치")]
    [Header("시작 지점. 0번 발판 위치")]
    [SerializeField] private Vector3 startPosition = new Vector3(10f, 0f, -10f);

    [Tooltip("칸 사이 간격. 발판 Scale이 6이면 최소 6 이상 권장")]
    [Header("칸 사이 간격. 발판 Scale이 6이면 최소 6 이상 권장")]
    [SerializeField] private float cellSpacing = 8f;

    [Tooltip("모든 발판 스케일")]
    [Header("모든 발판 스케일")]
    [SerializeField] private Vector3 cellScale = new Vector3(6f, 6f, 6f);
    [SerializeField] private Vector3 cellRotationEuler = Vector3.zero;

    [Header("Default Land Price")]
    [SerializeField] private int defaultBuyCost = 100000;
    [SerializeField] private int defaultTollCost = 20000;

    [Header("Default Build Cost")]
    [SerializeField] private int defaultSmallHouseBuildCost = 30000;
    [SerializeField] private int defaultHouseBuildCost = 50000;
    [SerializeField] private int defaultBigHouseBuildCost = 80000;
    [SerializeField] private int defaultHotelBuildCost = 120000;

    [Header("Default Build Toll")]
    [SerializeField] private int defaultSmallHouseToll = 10000;
    [SerializeField] private int defaultHouseToll = 30000;
    [SerializeField] private int defaultBigHouseToll = 60000;
    [SerializeField] private int defaultHotelToll = 100000;

    [Header("Default Special Amount")]
    [SerializeField] private int defaultTaxCost = 100000;
    [Header("Default Bonus Random Amount")]
    [SerializeField] private int defaultBonusMinAmount = 10000;
    [SerializeField] private int defaultBonusMaxAmount = 50000;
    [Header("defaultTravelCost")]
    [SerializeField] private int defaultTravelCost = 100000;

    [Header("Special Cells")]
    [SerializeField]
    private SpecialCellSetting[] specialCells =
{
    // 각 모서리 특수 칸
    new SpecialCellSetting
    {
        cellIndex = 0,
        cellType = BulmabulCellType.Start,
        customName = "시작"
    },

    new SpecialCellSetting
    {
        cellIndex = 40,
        cellType = BulmabulCellType.Jail,
        customName = "감옥"
    },

    new SpecialCellSetting
    {
        cellIndex = 80,
        cellType = BulmabulCellType.Travel,
        customName = "여행"
    },

    new SpecialCellSetting
    {
        cellIndex = 120,
        cellType = BulmabulCellType.Tax,
        customName = "세금"
    },

    // 보너스 칸 - 40칸 구간마다 1개씩
    new SpecialCellSetting
    {
        cellIndex = 20,
        cellType = BulmabulCellType.Bonus,
        customName = "보너스",
        bonusMinAmount = 10000,
        bonusMaxAmount = 30000
    },

    new SpecialCellSetting
    {
        cellIndex = 60,
        cellType = BulmabulCellType.Bonus,
        customName = "보너스",
        bonusMinAmount = 10000,
        bonusMaxAmount = 30000
    },

    new SpecialCellSetting
    {
        cellIndex = 100,
        cellType = BulmabulCellType.Bonus,
        customName = "보너스",
        bonusMinAmount = 10000,
        bonusMaxAmount = 30000
    },

    new SpecialCellSetting
    {
        cellIndex = 140,
        cellType = BulmabulCellType.Bonus,
        customName = "보너스",
        bonusMinAmount = 10000,
        bonusMaxAmount = 30000
    },

    // 찬스 칸 - 전체 맵에서 2개만
    new SpecialCellSetting
    {
        cellIndex = 70,
        cellType = BulmabulCellType.Chance,
        customName = "찬스"
    },

    new SpecialCellSetting
    {
        cellIndex = 150,
        cellType = BulmabulCellType.Chance,
        customName = "찬스"
    },
};

    private readonly List<BulmabulCellData> generatedCellDatas = new List<BulmabulCellData>();

    public IReadOnlyList<BulmabulCellData> GeneratedCellDatas => generatedCellDatas;

    private void Start()
    {
        if (generateOnStart)
            Generate();
    }

    public bool IsGenerated { get; private set; }

    public int TotalCellCount => totalCellCount;

    public bool IsBoardReady()
    {
        return board != null && board.CellCount == totalCellCount;
    }

    /// <summary>
    /// 아직 맵이 생성되지 않았으면 생성한다.
    /// GameState가 게임 시작 전에 호출한다.
    /// </summary>
    public bool GenerateIfNeeded()
    {
        if (IsGenerated && IsBoardReady())
            return true;

        Generate();

        return IsGenerated && IsBoardReady();
    }

    [ContextMenu("Generate Board")]
    public void Generate()
    {
        IsGenerated = false;

        if (landCellPrefab == null)
        {
            Debug.LogError("[BulmabulBoardGenerator] Land Cell Prefab이 비어있습니다.");
            return;
        }

        if (cellRoot == null)
            cellRoot = transform;

        if (totalCellCount <= 0)
        {
            Debug.LogError("[BulmabulBoardGenerator] totalCellCount가 0 이하입니다.");
            return;
        }

        if (cellsPerSide <= 0)
        {
            Debug.LogError("[BulmabulBoardGenerator] cellsPerSide가 0 이하입니다.");
            return;
        }

        if (totalCellCount != cellsPerSide * 4)
        {
            Debug.LogWarning($"[BulmabulBoardGenerator] totalCellCount({totalCellCount})가 cellsPerSide({cellsPerSide}) * 4와 다릅니다.");
        }

        ClearGeneratedCells();
        generatedCellDatas.Clear();

        for (int i = 0; i < totalCellCount; i++)
        {
            SpecialCellSetting setting = GetSpecialSetting(i);
            BulmabulCellType type = setting != null ? setting.cellType : BulmabulCellType.Land;

            GameObject prefab = GetPrefab(type, setting);
            Vector3 position = GetCellPosition(i);
            Quaternion rotation = Quaternion.Euler(cellRotationEuler);

            GameObject cellObj = Instantiate(prefab, position, rotation, cellRoot);
            cellObj.transform.localScale = GetCellScale(type);

            // 예전 자동 라벨 컴포넌트 제거.
            // 이 컴포넌트가 BulmabulBoardCellView에서 계산한 텍스트 위치를 다시 덮어써서
            // 39번, Jail, 42번 이후 오른쪽 라인 텍스트가 안 보이는 문제가 생긴다.
            RemoveLegacyLabelLayouts(cellObj);

            BulmabulCellData data = CreateCellData(i, type, setting);
            data.point = cellObj.transform;

            BulmabulBoardCellView view = cellObj.GetComponent<BulmabulBoardCellView>();
            if (view == null)
                view = cellObj.AddComponent<BulmabulBoardCellView>();

            view.Setup(i, data);

            BulmabulCellLabelLayout labelLayout = cellObj.GetComponent<BulmabulCellLabelLayout>();
            if (labelLayout == null)
                labelLayout = cellObj.AddComponent<BulmabulCellLabelLayout>();

            // 재배치 스크립트가 현재 셀 번호 / 셀 타입 / 한 변 칸 수를 알 수 있게 넘겨준다.
            // 이걸 해야 39번 Cell, Jail Cell, 40~79 오른쪽 라인 보정이 가능하다.
            labelLayout.Setup(i, type, cellsPerSide);

            generatedCellDatas.Add(data);
        }

        ApplyToBoard();

        IsGenerated = board != null && board.CellCount == totalCellCount;

        if (!IsGenerated)
        {
            Debug.LogError(
                $"[BulmabulBoardGenerator] 보드 생성 실패. Board CellCount = {(board != null ? board.CellCount : -1)}, Need = {totalCellCount}"
            );
            return;
        }

        // 보드 생성이 완전히 끝난 뒤,
        // 모든 셀의 이름/가격 텍스트 위치를 보드 중앙 기준으로 다시 배치한다.
        board.RefreshAllCellLabelLayouts();

        Debug.Log($"[BulmabulBoardGenerator] Board generated. Count = {generatedCellDatas.Count}");
    }

    private void RemoveLegacyLabelLayouts(GameObject cellObj)
    {
        if (cellObj == null)
            return;

        BulmabulCellLabelLayout[] layouts = cellObj.GetComponentsInChildren<BulmabulCellLabelLayout>(true);

        for (int i = 0; i < layouts.Length; i++)
        {
            if (layouts[i] == null)
                continue;

            if (Application.isPlaying)
                Destroy(layouts[i]);
            else
                DestroyImmediate(layouts[i]);
        }
    }

    [ContextMenu("Clear Board")]
    public void ClearGeneratedCells()
    {
        if (cellRoot == null)
            return;

        for (int i = cellRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = cellRoot.GetChild(i);

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        IsGenerated = false;
    }

    private void ApplyToBoard()
    {
        if (board == null)
        {
            Debug.LogWarning("[BulmabulBoardGenerator] Board가 비어있어서 Cells 자동 등록을 건너뜁니다.");
            return;
        }

        board.SetCellsFromGeneratedMap(generatedCellDatas);
    }

    /// <summary>
    /// 칸 타입별 스케일 반환.
    /// 일반 칸은 cellScale을 사용하고,
    /// Jail 칸만 작게 3,3,3으로 사용한다.
    /// </summary>
    private Vector3 GetCellScale(BulmabulCellType type)
    {
        if (type == BulmabulCellType.Jail)
            return new Vector3(3f, 3f, 3f);

        return cellScale;
    }

    private Vector3 GetCellPosition(int index)
    {
        int side = index / cellsPerSide;
        int local = index % cellsPerSide;

        switch (side)
        {
            // 0 ~ 39
            // 아래쪽 라인: 왼쪽 아래에서 오른쪽으로 이동
            case 0:
                return startPosition + new Vector3(local * cellSpacing, 0f, 0f);

            // 40 ~ 79
            // 오른쪽 라인: 오른쪽 아래에서 위로 이동
            // local + 1을 사용해서 Cell 39와 Cell 40이 겹치지 않게 함
            case 1:
                return startPosition + new Vector3(
                    (cellsPerSide - 1) * cellSpacing,
                    0f,
                    (local + 1) * cellSpacing
                );

            // 80 ~ 119
            // 위쪽 라인: 오른쪽 위에서 왼쪽으로 이동
            case 2:
                return startPosition + new Vector3(
                    (cellsPerSide - 2 - local) * cellSpacing,
                    0f,
                    cellsPerSide * cellSpacing
                );

            // 120 ~ 159
            // 왼쪽 라인: 왼쪽 위에서 아래로 이동
            case 3:
                return startPosition + new Vector3(
                    -cellSpacing,
                    0f,
                    (cellsPerSide - 1 - local) * cellSpacing
                );

            default:
                return startPosition;
        }
    }

    private SpecialCellSetting GetSpecialSetting(int index)
    {
        if (specialCells == null)
            return null;

        for (int i = 0; i < specialCells.Length; i++)
        {
            SpecialCellSetting setting = specialCells[i];

            if (setting == null)
                continue;

            if (setting.cellIndex == index)
                return setting;
        }

        return null;
    }

    private GameObject GetPrefab(BulmabulCellType type, SpecialCellSetting setting)
    {
        if (setting != null && setting.overridePrefab != null)
            return setting.overridePrefab;

        switch (type)
        {
            case BulmabulCellType.Start:
                return startCellPrefab != null ? startCellPrefab : landCellPrefab;

            case BulmabulCellType.Tax:
                return taxCellPrefab != null ? taxCellPrefab : landCellPrefab;

            case BulmabulCellType.Bonus:
                return bonusCellPrefab != null ? bonusCellPrefab : landCellPrefab;

            case BulmabulCellType.Chance:
                return chanceCellPrefab != null ? chanceCellPrefab : landCellPrefab;

            case BulmabulCellType.Jail:
                return jailCellPrefab != null ? jailCellPrefab : landCellPrefab;

            case BulmabulCellType.Travel:
                return travelCellPrefab != null ? travelCellPrefab : landCellPrefab;

            case BulmabulCellType.Land:
            default:
                return landCellPrefab;
        }
    }

    private BulmabulCellData CreateCellData(int index, BulmabulCellType type, SpecialCellSetting setting)
    {
        BulmabulCellData data = new BulmabulCellData();

        data.cellType = type;
        data.cellName = GetCellName(index, type, setting);
        data.point = null;

        LandPriceTier tier = GetPriceTier(index);

        if (type == BulmabulCellType.Land)
        {
            data.buyCost = Pick(setting != null ? setting.buyCost : 0, tier != null ? tier.buyCost : defaultBuyCost);
            data.tollCost = Pick(setting != null ? setting.tollCost : 0, tier != null ? tier.tollCost : defaultTollCost);

            data.smallHouseBuildCost = Pick(setting != null ? setting.smallHouseBuildCost : 0, tier != null ? tier.smallHouseBuildCost : defaultSmallHouseBuildCost);
            data.houseBuildCost = Pick(setting != null ? setting.houseBuildCost : 0, tier != null ? tier.houseBuildCost : defaultHouseBuildCost);
            data.bigHouseBuildCost = Pick(setting != null ? setting.bigHouseBuildCost : 0, tier != null ? tier.bigHouseBuildCost : defaultBigHouseBuildCost);
            data.hotelBuildCost = Pick(setting != null ? setting.hotelBuildCost : 0, tier != null ? tier.hotelBuildCost : defaultHotelBuildCost);

            data.smallHouseToll = Pick(setting != null ? setting.smallHouseToll : 0, tier != null ? tier.smallHouseToll : defaultSmallHouseToll);
            data.houseToll = Pick(setting != null ? setting.houseToll : 0, tier != null ? tier.houseToll : defaultHouseToll);
            data.bigHouseToll = Pick(setting != null ? setting.bigHouseToll : 0, tier != null ? tier.bigHouseToll : defaultBigHouseToll);
            data.hotelToll = Pick(setting != null ? setting.hotelToll : 0, tier != null ? tier.hotelToll : defaultHotelToll);

            data.lineId = setting != null && setting.lineId >= 0
                ? setting.lineId
                : tier != null ? tier.lineId : -1;

            data.isLandmark = setting != null && setting.isLandmark;
        }
        else
        {
            data.buyCost = 0;
            data.tollCost = 0;

            data.smallHouseBuildCost = 0;
            data.houseBuildCost = 0;
            data.bigHouseBuildCost = 0;
            data.hotelBuildCost = 0;

            data.smallHouseToll = 0;
            data.houseToll = 0;
            data.bigHouseToll = 0;
            data.hotelToll = 0;

            data.lineId = setting != null ? setting.lineId : -1;
            data.isLandmark = setting != null && setting.isLandmark;
        }

        data.taxCost = type == BulmabulCellType.Tax
            ? Pick(setting != null ? setting.taxCost : 0, defaultTaxCost)
            : 0;

        if (type == BulmabulCellType.Bonus)
        {
            data.bonusMinAmount = Pick(
                setting != null ? setting.bonusMinAmount : 0,
                defaultBonusMinAmount
            );

            data.bonusMaxAmount = Pick(
                setting != null ? setting.bonusMaxAmount : 0,
                defaultBonusMaxAmount
            );

            if (data.bonusMaxAmount < data.bonusMinAmount)
                data.bonusMaxAmount = data.bonusMinAmount;
        }
        else
        {
            data.bonusMinAmount = 0;
            data.bonusMaxAmount = 0;
        }

        data.travelCost = type == BulmabulCellType.Travel
            ? Pick(setting != null ? setting.travelCost : 0, defaultTravelCost)
            : 0;

        return data;
    }

    private int Pick(int overrideValue, int defaultValue)
    {
        return overrideValue > 0 ? overrideValue : defaultValue;
    }

    private string GetCellName(int index, BulmabulCellType type, SpecialCellSetting setting)
    {
        // setting에 customName을 직접 넣은 경우에는 그 이름을 우선 사용
        if (setting != null && !string.IsNullOrWhiteSpace(setting.customName))
            return setting.customName;

        bool isEng = IsEnglish();

        switch (type)
        {
            case BulmabulCellType.Start:
                return isEng ? "Start" : "시작";

            case BulmabulCellType.Tax:
                return isEng ? "Tax" : "세금";

            case BulmabulCellType.Bonus:
                return isEng ? "Bonus" : "보너스";

            case BulmabulCellType.Chance:
                return isEng ? "Chance" : "찬스";

            case BulmabulCellType.Jail:
                return isEng ? "Jail" : "감옥";

            case BulmabulCellType.Travel:
                return isEng ? "Travel" : "여행";           

            case BulmabulCellType.Land:
            default:
                return GetMapLandName(index);
        }
    }

    private string GetMapLandName(int index)
    {
        bool isEng = IsEnglish();

        string[] names;

        if (IsUsaMap())
            names = isEng ? usaLandNamesEng : usaLandNamesKor;
        else
            names = isEng ? koreaLandNamesEng : koreaLandNamesKor;

        if (names == null || names.Length == 0)
            return isEng ? $"Land {index}" : $"땅 {index}";

        int nameIndex = GetLandOrderIndex(index);

        if (nameIndex < 0)
            nameIndex = index;

        nameIndex = Mathf.Abs(nameIndex) % names.Length;

        return names[nameIndex];
    }

    /// <summary>
    /// 특수칸을 제외하고 일반 땅 순서만 계산한다.
    /// 예: 0번 시작, 20번 찬스 같은 칸은 이름 순서에서 제외.
    /// </summary>
    private int GetLandOrderIndex(int index)
    {
        int order = 0;

        for (int i = 0; i <= index; i++)
        {
            SpecialCellSetting setting = GetSpecialSetting(i);
            BulmabulCellType type = setting != null ? setting.cellType : BulmabulCellType.Land;

            if (type != BulmabulCellType.Land)
                continue;

            if (i == index)
                return order;

            order++;
        }

        return index;
    }

    private int GetSelectedMapIndex()
    {
        if (NetWorkLauncher.instance != null)
            return NetWorkLauncher.instance.SelectedMapIndex;

        return 0; // 기본값: 한국맵
    }

    private bool IsUsaMap()
    {
        return GetSelectedMapIndex() == 1;
    }

    private bool IsEnglish()
    {
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Eng;
    }
}