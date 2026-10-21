using System;
using System.Collections.Generic;
using System.Linq;

namespace LiveTranslateOverlay.Core.Text;

public static class TextSentenceTokenizer
{
    public static List<string> SplitToWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static bool EndsWithSentencePunctuation(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        char last = word[^1];
        return last is '.' or '?' or '!' or ':';
    }

    public static List<List<T>> GroupIntoSentences<T>(IReadOnlyList<T> items, Func<T, string> textSelector)
    {
        var sentences = new List<List<T>>();
        var current = new List<T>();

        foreach (var item in items)
        {
            current.Add(item);
            if (EndsWithSentencePunctuation(textSelector(item)))
            {
                sentences.Add(current);
                current = new List<T>();
            }
        }

        if (current.Count > 0)
        {
            sentences.Add(current);
        }

        return sentences;
    }

    public static List<string> TrimOverflowingSentences(
        IReadOnlyList<string> words,
        int maxCharacters,
        out bool wasTruncated)
    {
        wasTruncated = false;
        var sentences = GroupIntoSentences(words, w => w);

        int CalculateTotalLength() => string.Join(" ", sentences.SelectMany(s => s)).Length;

        while (CalculateTotalLength() > maxCharacters && sentences.Count > 1)
        {
            sentences.RemoveAt(0);
            wasTruncated = true;
        }

        return sentences.SelectMany(s => s).ToList();
    }
}