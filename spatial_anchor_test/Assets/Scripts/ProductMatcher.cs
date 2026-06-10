using System;
using System.Collections.Generic;
using UnityEngine;

// ProductMatcher v0.7.0 — Hierarchical 매칭.
//   1단계: CLIP top-K → category (cola, laptop, ...)
//   2단계: OCR text → category 내 brand (coca-cola, pepsi, macbook, ...)
//   OCR 가 brand 확정 못 함 → 광고 안 표시 (false positive 방지)
//
// JSON schema (v0.7.0):
//   categories: [{name, embeddings_flat, n_refs, brands:[{name, keywords, ad_image, comparison...}]}]
public class ProductMatcher : MonoBehaviour
{
    [Header("DB 설정")]
    // Windows repo 환경: metadata.json + .npy 분리 schema 만 존재 (Mac 의 build_*_db.py
    // 산출물인 unity_db.json 은 없음). LoadCoroutine 이 array-root json 을 감지하면
    // 자동으로 .npy 까지 읽어 DbRoot 로 변환한다.
    public string dbFilename = "db/metadata.json";

    [Tooltip("최소 코사인 유사도 (category 매칭). 미달 시 매칭 안 함.")]
    [Range(0.0f, 1.0f)]
    public float minSimilarity = 0.55f;

    [Header("v0.5.14 Top-k 매칭")]
    [Tooltip("category 매칭 시 각 ref 의 sim 의 top-K 평균.")]
    [Range(1, 10)] public int topK = 1;

    [Header("v0.7.0 OCR fail 정책")]
    [Tooltip("true: category 매칭됐는데 OCR 가 brand 확정 못 하면 null 반환 (광고 안 표시).\n" +
             "false: brands[0] (첫 brand) fallback.")]
    public bool strictBrandRequired = true;

    [Header("v0.7.3 CLIP brand fallback 게이트")]
    [Tooltip("기본 OFF — 브랜드는 OCR 이 결정 (설계 원칙: CLIP=category, OCR=brand).\n" +
             "OCR 실패 시 CLIP brand-specific 임베딩으로 brand 를 추정하던 경로는\n" +
             "환경 bias 로 항상 coca-cola 를 찍어 펩시를 코크로 오판 (시연 #13).\n" +
             "ON: 데모 escape hatch (OCR 미성숙 시 광고라도 띄우고 싶을 때).")]
    public bool enableClipBrandFallback = false;

    [Header("상태")]
    public bool isReady;
    public string statusMessage = "초기화 중...";
    public int categoryCount;

    // ──────── DB classes ────────
    [Serializable]
    public class Brand
    {
        public string name;
        public string[] keywords;
        public string[] negative_keywords;
        // v0.7.1: brand-specific embedding (optional). OCR fail 시 CLIP brand 매칭 fallback.
        public float[] embeddings_flat;
        public int n_refs;
        public string ad_image;
        public string ad_label;
        public string identified_name;
        public string ad_brand;
        public float rating;
        public string price;
        public string differentiator;
        public string location;
    }

    [Serializable]
    public class Category
    {
        public int id;
        public string name;
        public string category_label;
        public float[] embeddings_flat;
        public int n_refs;
        public Brand[] brands;
    }

    [Serializable]
    class DbRoot
    {
        public string model;
        public int dim;
        public string schema_version;
        public Category[] categories;
    }

    Category[] _categories;
    int _dim;

    // 매칭 결과 — 비교 카드/광고 표시에 쓰임.
    public class MatchResult
    {
        public Category category;
        public Brand brand;
        public float categoryScore;
        public string brandSource;   // "ocr" / "clip" / "default"
        public float brandScore;
    }

    void Start() { StartCoroutine(LoadCoroutine()); }

    public System.Collections.IEnumerator LoadCoroutine()
    {
        string srcPath = System.IO.Path.Combine(Application.streamingAssetsPath, dbFilename);
        string json = null;

        if (Application.platform == RuntimePlatform.Android)
        {
            var req = UnityEngine.Networking.UnityWebRequest.Get(srcPath);
            yield return req.SendWebRequest();
            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                statusMessage = $"❌ {dbFilename} 로드 실패: {req.error}";
                Debug.LogError($"[ProductMatcher] {statusMessage}");
                yield break;
            }
            json = req.downloadHandler.text;
        }
        else
        {
            if (!System.IO.File.Exists(srcPath))
            {
                statusMessage = $"❌ {dbFilename} 없음: {srcPath}";
                Debug.LogError($"[ProductMatcher] {statusMessage}");
                yield break;
            }
            json = System.IO.File.ReadAllText(srcPath);
        }

        DbRoot db = null;
        string trimmed = (json ?? "").TrimStart();
        bool isMetadataSchema = trimmed.Length > 0 && trimmed[0] == '[';
        if (isMetadataSchema)
        {
            // metadata.json (array root + .npy 분리) → DbRoot 로 inline 변환.
            // 별도 코루틴: .npy 를 UnityWebRequest 로 읽어와야 하므로 yield 가능해야 함.
            DbRoot built = null;
            yield return BuildDbFromMetadata(json, r => built = r);
            db = built;
            if (db == null)
            {
                statusMessage = "❌ metadata.json 변환 실패 (위 로그 참조)";
                yield break;
            }
        }
        else
        {
            try { db = JsonUtility.FromJson<DbRoot>(json); }
            catch (Exception e)
            {
                statusMessage = $"❌ JSON 파싱 실패: {e.Message}";
                Debug.LogError($"[ProductMatcher] {statusMessage}");
                yield break;
            }
        }

        if (db == null || db.categories == null || db.categories.Length == 0)
        {
            statusMessage = "❌ DB 비었음";
            yield break;
        }

        _categories = db.categories;
        _dim = db.dim;
        categoryCount = _categories.Length;
        isReady = true;
        int brandTotal = 0; foreach (var c in _categories) brandTotal += c.brands?.Length ?? 0;
        statusMessage = $"✅ DB 로드: {categoryCount} categories, {brandTotal} brands (model={db.model}, dim={db.dim}, schema={db.schema_version})";
        Debug.Log($"[ProductMatcher] {statusMessage}");

        for (int i = 0; i < _categories.Length; i++)
        {
            var c = _categories[i];
            int expected = c.n_refs * db.dim;
            int got = c.embeddings_flat?.Length ?? 0;
            if (got != expected || c.n_refs <= 0)
                Debug.LogWarning($"[ProductMatcher] '{c.name}' embeddings_flat 길이 불일치 expected {expected} got {got}");
        }
    }

    /// <summary>
    /// Stage 1 — CLIP embedding → category 매칭 (top-K). brand 는 안 봄.
    /// null = category 미매칭 (이 경우 HelloAR 는 OCR 도 skip).
    /// v0.7.3: category 분류를 brand 해석과 분리 — "CLIP 이 먼저 무슨 물체인지 정하고,
    ///         그 다음 (category 매칭된 것만) OCR 로 brand 확정" 흐름을 코드로 명시.
    /// </summary>
    public Category MatchCategory(float[] embedding, out float score)
    {
        score = -1f;
        if (!isReady || _categories == null || embedding == null || embedding.Length == 0) return null;
        if (embedding.Length != _dim) return null;

        Category bestCat = null;
        float bestScore = -1f;
        var allSims = new System.Text.StringBuilder();
        foreach (var c in _categories)
        {
            if (c.embeddings_flat == null || c.n_refs <= 0) continue;
            if (c.embeddings_flat.Length != c.n_refs * _dim) continue;

            float[] sims = new float[c.n_refs];
            for (int r = 0; r < c.n_refs; r++)
            {
                float dot = 0f;
                int offset = r * _dim;
                for (int j = 0; j < _dim; j++) dot += c.embeddings_flat[offset + j] * embedding[j];
                sims[r] = dot;
            }
            Array.Sort(sims);   // ascending
            int k = Math.Min(Math.Max(1, topK), c.n_refs);
            float sum = 0f;
            for (int t = 0; t < k; t++) sum += sims[c.n_refs - 1 - t];
            float s = sum / k;
            float top1 = sims[c.n_refs - 1];

            allSims.Append($" {c.name}=top1:{top1:F3}|top{k}avg:{s:F3}");
            if (s > bestScore) { bestScore = s; bestCat = c; }
        }

        if (bestCat == null || bestScore < minSimilarity)
        {
            Debug.Log($"[ProductMatcher] STAGE1: no category match. sims:{allSims} thr={minSimilarity:F2}");
            return null;
        }
        Debug.Log($"[ProductMatcher] STAGE1 OK: category={bestCat.name} score={bestScore:F3} sims:{allSims}");
        score = bestScore;
        return bestCat;
    }

    /// <summary>
    /// Stage 2 — 이미 매칭된 category 안에서 OCR text 로 brand 확정.
    /// 1) OCR keyword 매칭 (primary, deterministic) → brand.
    /// 2) (옵션, 기본 OFF) OCR 실패 시 CLIP brand-specific 임베딩 fallback.
    /// brand 못 찾으면 null (strict → 광고 X).
    /// </summary>
    public Brand ResolveBrand(Category cat, float[] embedding, string ocrText,
                              out string brandSource, out float brandScore)
    {
        brandSource = "none";
        brandScore = 0f;
        if (cat == null || cat.brands == null) return null;

        string ocrLower = (ocrText ?? "").ToLowerInvariant();
        Brand matchedBrand = null;

        // 2-1. OCR keyword primary
        if (!string.IsNullOrEmpty(ocrLower))
        {
            foreach (var b in cat.brands)
            {
                if (b == null) continue;
                bool neg = false;
                if (b.negative_keywords != null)
                    foreach (var nk in b.negative_keywords)
                        if (!string.IsNullOrEmpty(nk) && ocrLower.Contains(nk.ToLowerInvariant()))
                        { neg = true; break; }
                if (neg) continue;
                if (b.keywords != null)
                    foreach (var kw in b.keywords)
                        if (!string.IsNullOrEmpty(kw) && ocrLower.Contains(kw.ToLowerInvariant()))
                        { matchedBrand = b; brandSource = "ocr"; brandScore = 1f; break; }
                if (matchedBrand != null) break;
            }
        }

        // 2-2. (기본 OFF) OCR 실패 + brand 다수 → CLIP brand-specific fallback.
        //      환경 bias 로 항상 한 brand 만 찍어 펩시를 코크로 오판 (시연 #13) → 기본 비활성.
        if (enableClipBrandFallback && matchedBrand == null && cat.brands.Length > 1 && embedding != null)
        {
            float topBrandScore = -1f;
            var brandDbg = new System.Text.StringBuilder();
            foreach (var b in cat.brands)
            {
                if (b.embeddings_flat == null || b.n_refs <= 0) continue;
                if (b.embeddings_flat.Length != b.n_refs * _dim) continue;
                float[] sims = new float[b.n_refs];
                for (int r = 0; r < b.n_refs; r++)
                {
                    float dot = 0f;
                    int offset = r * _dim;
                    for (int j = 0; j < _dim; j++) dot += b.embeddings_flat[offset + j] * embedding[j];
                    sims[r] = dot;
                }
                Array.Sort(sims);
                int k = Math.Min(Math.Max(1, topK), b.n_refs);
                float sum = 0f;
                for (int t = 0; t < k; t++) sum += sims[b.n_refs - 1 - t];
                float s = sum / k;
                brandDbg.Append($" {b.name}:{s:F3}");
                if (s > topBrandScore) { topBrandScore = s; matchedBrand = b; }
            }
            if (matchedBrand != null)
            {
                brandSource = "clip";
                brandScore = topBrandScore;
                Debug.Log($"[ProductMatcher] Stage2 CLIP brand fallback:{brandDbg} → {matchedBrand.name} ({topBrandScore:F3})");
            }
        }

        if (matchedBrand == null)
            Debug.Log($"[ProductMatcher] STAGE2 FAIL — category={cat.name} 안에서 brand 확정 못 함 (ocr='{ocrLower.Replace('\n', ' ')}').");
        else
            Debug.Log($"[ProductMatcher] ✅ category={cat.name} brand={matchedBrand.name} src={brandSource} bscore={brandScore:F3} ocr='{ocrLower.Replace('\n', ' ')}'");
        return matchedBrand;
    }

    /// <summary>
    /// 편의 wrapper (category → brand 한 번에). OCR 을 먼저 다 뽑아두고 부를 때 사용.
    /// HelloAR 는 OCR 을 category 매칭 후에만 돌리려고 MatchCategory/ResolveBrand 를 따로 호출.
    /// </summary>
    public MatchResult FindBestMatch(float[] embedding, string ocrText)
    {
        var cat = MatchCategory(embedding, out float catScore);
        if (cat == null) return null;
        var brand = ResolveBrand(cat, embedding, ocrText, out string src, out float bscore);
        if (brand == null) return null;
        return new MatchResult {
            category = cat, brand = brand, categoryScore = catScore,
            brandSource = src, brandScore = bscore,
        };
    }

    /// <summary>
    /// v1.2 — category 안에서 이름으로 brand 조회 (read-only helper, 대소문자 무시).
    /// 색 기반 brand 판별기(HelloAR.ResolveBrandByColor)가 "coca-cola"/"pepsi" 를 brand 객체로 매핑할 때 사용.
    /// 코어 매칭 로직(MatchCategory/ResolveBrand)은 건드리지 않음.
    /// </summary>
    public Brand GetBrandByName(Category cat, string name)
    {
        if (cat == null || cat.brands == null || string.IsNullOrEmpty(name)) return null;
        foreach (var b in cat.brands)
        {
            if (b == null || b.name == null) continue;
            if (string.Equals(b.name, name, StringComparison.OrdinalIgnoreCase)) return b;
        }
        return null;
    }

    // ──────── metadata.json schema 어댑터 (Windows repo 전용) ────────
    // Mac 의 build_*_db.py 가 생성하는 unity_db.json 이 없으므로 metadata.json + .npy 파일을
    // 런타임에 평탄화한다. embedding 은 numpy V1.0 (.npy) — 10B magic + 2B header_len + ASCII header
    // + float32 body. header 의 shape 에서 (n_refs, dim) 을 직접 파싱한다.
    [Serializable] class MetaComparison
    {
        public string identified_name;
        public string ad_brand;
        public float rating;
        public string price;
        public string differentiator;
        public string location;
    }
    [Serializable] class MetaBrand
    {
        public string name;
        public string[] keywords;
        public string[] negative_keywords;
        public string embedding;
        public string ad_image;
        public string ad_label;
        public MetaComparison comparison;
    }
    [Serializable] class MetaCategory
    {
        public int id;
        public string name;
        public string category_label;
        public string embedding;
        public MetaBrand[] brands;
    }
    [Serializable] class MetaArrayWrap { public MetaCategory[] items; }

    System.Collections.IEnumerator BuildDbFromMetadata(string json, Action<DbRoot> done)
    {
        // JsonUtility 는 top-level 배열을 지원 안 함 → "{\"items\":[...]}" 로 감싼다.
        string wrapped = "{\"items\":" + json + "}";
        MetaArrayWrap wrap;
        try { wrap = JsonUtility.FromJson<MetaArrayWrap>(wrapped); }
        catch (Exception e) { Debug.LogError($"[ProductMatcher] metadata.json 파싱 실패: {e.Message}"); done(null); yield break; }
        if (wrap == null || wrap.items == null || wrap.items.Length == 0)
        { Debug.LogError("[ProductMatcher] metadata.json 비었음"); done(null); yield break; }

        var cats = new List<Category>(wrap.items.Length);
        int dim = 0;
        foreach (var mc in wrap.items)
        {
            float[] catEmb = null; int catRefs = 0; int catDim = 0;
            yield return LoadNpy(mc.embedding, (e, n, d) => { catEmb = e; catRefs = n; catDim = d; });
            if (catEmb == null)
            {
                Debug.LogWarning($"[ProductMatcher] category '{mc.name}' embedding 로드 실패 ({mc.embedding}) — skip");
                continue;
            }
            if (dim == 0) dim = catDim;
            else if (dim != catDim) Debug.LogWarning($"[ProductMatcher] dim 불일치 cat={mc.name} ({catDim} vs {dim})");

            var brands = new List<Brand>(mc.brands?.Length ?? 0);
            if (mc.brands != null)
            {
                foreach (var mb in mc.brands)
                {
                    float[] bEmb = null; int bRefs = 0; int bDim = 0;
                    if (!string.IsNullOrEmpty(mb.embedding))
                        yield return LoadNpy(mb.embedding, (e, n, d) => { bEmb = e; bRefs = n; bDim = d; });
                    var b = new Brand {
                        name = mb.name,
                        keywords = mb.keywords,
                        negative_keywords = mb.negative_keywords,
                        embeddings_flat = bEmb,
                        n_refs = bRefs,
                        ad_image = mb.ad_image,
                        ad_label = mb.ad_label,
                    };
                    if (mb.comparison != null)
                    {
                        b.identified_name = mb.comparison.identified_name;
                        b.ad_brand = mb.comparison.ad_brand;
                        b.rating = mb.comparison.rating;
                        b.price = mb.comparison.price;
                        b.differentiator = mb.comparison.differentiator;
                        b.location = mb.comparison.location;
                    }
                    brands.Add(b);
                }
            }
            cats.Add(new Category {
                id = mc.id, name = mc.name, category_label = mc.category_label,
                embeddings_flat = catEmb, n_refs = catRefs, brands = brands.ToArray(),
            });
        }
        done(new DbRoot {
            model = "mobileclip-s2", dim = dim,
            schema_version = "metadata-v1", categories = cats.ToArray(),
        });
    }

    // numpy V1.0 .npy reader. shape=(n,dim) float32 만 지원 (현 DB 가 모두 그 형태).
    System.Collections.IEnumerator LoadNpy(string relPath, Action<float[], int, int> done)
    {
        string full = System.IO.Path.Combine(Application.streamingAssetsPath, relPath);
        byte[] bytes = null;
        if (Application.platform == RuntimePlatform.Android)
        {
            var req = UnityEngine.Networking.UnityWebRequest.Get(full);
            yield return req.SendWebRequest();
            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            { Debug.LogWarning($"[ProductMatcher] .npy 로드 실패 {relPath}: {req.error}"); done(null, 0, 0); yield break; }
            bytes = req.downloadHandler.data;
        }
        else
        {
            if (!System.IO.File.Exists(full))
            { Debug.LogWarning($"[ProductMatcher] .npy 없음: {full}"); done(null, 0, 0); yield break; }
            bytes = System.IO.File.ReadAllBytes(full);
        }
        try
        {
            // 6B magic "\x93NUMPY" + 1B major + 1B minor + 2B header_len (LE) + ASCII header
            if (bytes.Length < 10 || bytes[0] != 0x93) throw new Exception("not a .npy");
            int hlen = bytes[8] | (bytes[9] << 8);
            string header = System.Text.Encoding.ASCII.GetString(bytes, 10, hlen);
            // shape: (n, dim)  — 정규식 없이 단순 파싱.
            int sIdx = header.IndexOf("'shape':");
            int lp = header.IndexOf('(', sIdx);
            int rp = header.IndexOf(')', lp);
            var parts = header.Substring(lp + 1, rp - lp - 1).Split(',');
            int n = int.Parse(parts[0].Trim());
            int d = int.Parse(parts[1].Trim());
            bool fortran = header.Contains("'fortran_order': True");
            if (fortran) throw new Exception("fortran_order 미지원");
            int floatCount = n * d;
            int bodyOffset = 10 + hlen;
            if (bytes.Length - bodyOffset < floatCount * 4) throw new Exception("body 길이 부족");
            float[] arr = new float[floatCount];
            Buffer.BlockCopy(bytes, bodyOffset, arr, 0, floatCount * 4);
            done(arr, n, d);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ProductMatcher] .npy 파싱 실패 {relPath}: {e.Message}");
            done(null, 0, 0);
        }
    }
}
