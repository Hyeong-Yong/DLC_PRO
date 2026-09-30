using System;
using System.Linq;
using ScottPlot;
using SkiaSharp;

namespace DLC_PRO.Controls;

public static class PlotTypography
{
    public static string FontName { get; } = FindFont();
    private static string FindFont()
    {
        string[] candidates = { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Noto Sans KR", "Apple SD Gothic Neo", "NanumGothic" };
        var installed = SKFontManager.Default.FontFamilies.ToArray();
        foreach (string family in candidates)
            if (installed.Contains(family, StringComparer.OrdinalIgnoreCase)) return family;
        using var fallback = SKFontManager.Default.MatchCharacter('가');
        return fallback?.FamilyName ?? Fonts.Default;
    }

    public static void Apply(Plot plot)
    {
        plot.Font.Set(FontName);
        plot.Axes.Title.Label.FontName = FontName;
        plot.Axes.Bottom.Label.FontName = FontName;
        plot.Axes.Left.Label.FontName = FontName;
        plot.Axes.Right.Label.FontName = FontName;
        plot.Legend.FontName = FontName;
    }
}
