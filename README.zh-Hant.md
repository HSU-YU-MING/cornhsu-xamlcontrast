# XamlContrast

**XAML 原始碼的對比度靜態稽核。不用開 App、一次掃完整個專案、低於 WCAG AA 就讓 CI 紅燈。**

[English](README.md)

現有的桌面對比檢查全部是「執行期＋手動＋一次一個元素」。XamlContrast 反過來：
解析 XAML 原始碼、算出**每段文字實際疊在什麼顏色上**、深淺兩主題各算一次 WCAG 對比，
低於 AA 就讓 CI 失敗。

```bash
dotnet tool install -g Cornhsu.XamlContrast
xamlcontrast 你的專案路徑
```

> 0.x 期間請 pin 確切版本（`--version 0.1.0`）；1.0 後介面凍結。

## 核心概念

每筆結果有兩個**獨立**維度：

- **等級**（絕對對比）：`fail`（< 門檻×2/3）／`warn`／`ok`／`decorative`
- **對稱**（跨主題）：`both-low`＝色票本身不夠，換主題救不了；
  `dark-fails`／`light-fails`＝設計意圖沒跨主題保住

對稱**永遠不會**被拿來藏結果 ——「兩邊一樣糟」不是刻意的證據，只是糟兩次。
低於 AA 的一律報出來，由人判斷。

## 難的不是 WCAG 公式

公式只有 20 行。價值在「這段文字實際疊在什麼顏色上」的**十二條解析規則**，
每一條都是在正式發行的產品上踩出來的：Transparent 穿透、alpha 合成、Opacity 沿樹累乘、
ControlTemplate 子樹、Style setter 配對、觸發態、死 setter 過濾、具名／行內 Style
（含 BasedOn 鏈）、同條件觸發器合併、停用態豁免（WCAG 1.4.3）、半透明色票、
模板根背景。

## 零設定色盤偵測

自動判斷色盤在哪：主題配對（Dark+Light 兩檔）→ C# 真相源（三元組陣列，
**提供兩套主題的來源優先於只有一套的**）→ 單一主題字典 → 都沒有就退回只算寫死色碼
並**大聲講**。猜錯可用 `xamlcontrast.config.json` 指定（[schema](docs/config-schema.md)）。

## 進 CI

```bash
xamlcontrast src/MyApp --json report.json    # 有 fail 就 exit 1
xamlcontrast src/MyApp --fail-on warn        # 嚴格模式：全部要過 AA
```

退出碼：失敗 `1`、**解析出 0 組配對也是 `1`**（空掃不是通過）、用法錯誤 `2`、其餘 `0`。

**既有專案導入（baseline ratchet）**：第一天全紅的門會被關掉。先凍結既有債，
之後只擋新增與惡化 —— 債只准減不准增：

```bash
xamlcontrast src/MyApp --write-baseline xamlcontrast-baseline.json   # 跑一次，commit 進 repo
xamlcontrast src/MyApp --baseline xamlcontrast-baseline.json         # CI 裡
```

**刻意低對比的標記**（理由必填，壓掉會計數，不會靜默消失）：

```xml
<!-- xamlcontrast-ignore: 浮水印，刻意低對比 -->
<TextBlock Opacity="0.4" Text="草稿" ... />
```

## 實戰成績

不是「demo 跑得動」——在**四個正式發行的 WPF 產品**上找到並促成修正 **250+ 處**真實對比問題；
CelFlow 已完全歸零（368 組配對全過）。過程中工具自己出過八次「看起來健康但錯」的報告，
每一次都變成回歸測試。這個專案用血寫成的第一原則：
**稽核工具最大的風險不是漏報，是謊報健康。**

## 已知限制

TargetName 模板 Setter、跨元素同條件觸發器（皆為假警報類，已逐筆實查）、
sibling 背景、圖片上的文字、隱含樣式；Binding 來的顏色標「無法解析」而不猜 ——
猜了就是拿誠實的不確定去換錯誤的信心。

## 授權

MIT © [許彧銘 Hsu Yu-Ming](https://cornhsu.com/)
