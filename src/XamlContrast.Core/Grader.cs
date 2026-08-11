namespace XamlContrast.Core;

/// <summary>
/// 分級與對稱是「兩個獨立的問題」，不可混為一談。
///
/// ⚠ 2026-07-30 修正的重大缺陷（先後在 CelFlow 與 Kindling 上實證）：
///   舊版把「兩個主題都低而且差不多」直接判成『刻意低對比（設計意圖）』並藏起來。
///   但對稱不是意圖的證據 —— 兩邊一樣糟就只是兩邊都糟。
///   「這是不是刻意的裝飾」只有看它在畫什麼才知道，工具不猜 —— 一律報出來由人判斷。
///
///   等級（絕對對比）：fail &lt; 門檻×2/3 ／ warn 其間 ／ ok ≥ 門檻 ／ decorative 不設限
///   對稱（跨主題）  ：single-theme ／ both-low（色票本身不夠，換主題救不了）
///                     dark-fails / light-fails（設計意圖沒跨主題保住）
/// </summary>
public static class Grader
{
    public static void Grade(AuditResult result)
    {
        // 單一主題：色盤裡沒有任何深淺不同的值（空色盤也算 —— 寫死色碼恆等）
        var singleTheme = result.Detection.Palette.IsSingleTheme;

        foreach (var f in result.Findings)
        {
            var worst = Math.Min(f.RatioDark, f.RatioLight);
            var best = Math.Max(f.RatioDark, f.RatioLight);
            f.Gap = Math.Round(best - worst, 2);

            // 門檻依元素而異：一般文字 4.5、大字級 3.0、裝飾元素不設限（Need = 0）；
            // failFactor 可由 config 覆寫（改它等於改稽核標準，Report 會標明非預設）
            f.Category =
                f.Need <= 0 ? Category.Decorative :
                worst >= f.Need ? Category.Ok :
                worst < f.Need * result.Config.Thresholds.FailFactor ? Category.Fail :
                Category.Warn;

            // 對稱只在「沒過」的配對上有意義 —— 問的是「這個問題換主題救不救得回來」。
            // 合格的配對沒有問題要救，硬分類會給出 21:1 卻標 both-low（＝色票太弱）
            // 這種自相矛盾的輸出。人看的報告只印 fail/warn 所以看不到，但 JSON 全印。
            f.Symmetry =
                f.Category is not (Category.Fail or Category.Warn) ? Symmetry.NotApplicable :
                singleTheme ? Symmetry.SingleTheme :
                f.Gap < 1.5 ? Symmetry.BothLow :
                f.RatioLight < f.RatioDark ? Symmetry.LightFails :
                Symmetry.DarkFails;
        }
    }
}
