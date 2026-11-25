// Copyright (c) Stephen Kraus
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// This file, TokenHighlightService.cs, is part of Jitendex.
//
// Jitendex is free software: you can redistribute it and/or modify it under the terms of
// the GNU Affero General Public License as published by the Free Software Foundation,
// either version 3 of the License or (at your option) any later version.
//
// Jitendex is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY,
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License along with Jitendex.
// If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Frozen;
using Jitendex.Data.Home;
using Jitendex.Data.Tatoeba;
using Jitendex.Process.Tatoeba.Models;
using Jitendex.Process.Tatoeba.Tables;
using Microsoft.Extensions.Logging;

namespace Jitendex.Process.Tatoeba.Services;

internal partial class TokenHighlightService
(
    ILogger<TokenHighlightService> logger,
    TatoebaForkContext tatoebaContext,
    HomeContext homeContext,
    TokenHighlightTable table
)
{
    public void Write()
    {
        var cache = LoadDisambiguationCache();
        var usedCacheKeys = new HashSet<(int, int, int)>(cache.Count);

        var priorityTokens = tatoebaContext.Tokens
            .Where(static t => t.IsPriority)
            .Select(static t => new
            {
                t.ExampleId,
                t.SegmentationOrder,
                t.Order,
                t.Headword,
                t.SentenceForm,
                ExampleText = t.Segmentation.Example.Text,
            });

        var rows = new List<TokenHighlightRow>();
        Span<int> indices = stackalloc int[128];

        foreach (var token in priorityTokens)
        {
            var text = token.SentenceForm ?? token.Headword;
            int index = 0;
            int count = 0;
            const StringComparison comparison = StringComparison.Ordinal;

            while ((index = token.ExampleText.IndexOf(text, index, comparison)) != -1)
            {
                indices[count++] = index;
                index += text.Length;
            }

            if (count == 0)
            {
                LogMissingSentenceText(token.ExampleId, text);
                continue;
            }

            if (count > 1)
            {
                var cacheKey = (token.ExampleId, token.SegmentationOrder, token.Order);
                if (!cache.TryGetValue(cacheKey, out var cachedIndices))
                {
                    LogMissingCacheExample(token.ExampleId, token.SegmentationOrder, token.Order, text);
                }
                else if (!IsSubset(cachedIndices, indices[..count]))
                {
                    LogInvalidCache(token.ExampleId, token.SegmentationOrder, token.Order, text);
                    usedCacheKeys.Add(cacheKey);
                }
                else
                {
                    usedCacheKeys.Add(cacheKey);
                    count = 0;
                    foreach (var cachedIndex in cachedIndices)
                        indices[count++] = cachedIndex;
                }
            }

            for (int i = 0; i < count; i++)
            {
                rows.Add(new
                (
                    ExampleId: token.ExampleId,
                    SegmentationOrder: token.SegmentationOrder,
                    TokenOrder: token.Order,
                    StartIndex: indices[i]
                ));
            }
        }

        foreach (var cacheKey in cache.Keys)
        {
            if (!usedCacheKeys.Contains(cacheKey))
                LogUnusedCacheKey(cacheKey.Item1, cacheKey.Item2, cacheKey.Item3);
        }

        table.InsertItems(tatoebaContext, rows);
    }

    private FrozenDictionary<(int, int, int), FrozenSet<int>> LoadDisambiguationCache()
        => homeContext.Tokens
            .Select(static t => new
            {
                Key = new { t.ExampleId, t.SegmentationOrder, t.Order },
                Values = t.Highlights
                    .OrderBy(static t => t.StartIndex)
                    .Select(static t => t.StartIndex)
                    .ToFrozenSet()
            })
            .ToFrozenDictionary
            (
                static x => (x.Key.ExampleId, x.Key.SegmentationOrder, x.Key.Order),
                static x => x.Values
            );

    private static bool IsSubset(FrozenSet<int> smallSet, ReadOnlySpan<int> bigSet)
    {
        foreach (var x in smallSet)
        {
            bool found = false;
            foreach (var y in bigSet)
            {
                if (x == y)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                return false;
        }
        return true;
    }

    [LoggerMessage(LogLevel.Warning,
    "Example ID {ExampleId} does not contain expected text `{Text}`.")]
    partial void LogMissingSentenceText(int exampleId, string text);

    [LoggerMessage(LogLevel.Warning,
    "Example ID {ExampleId}, segmentation order {SegOrder}, token order {TokenOrder} ({Text}) not found in disambiguation cache.")]
    partial void LogMissingCacheExample(int exampleId, int segOrder, int tokenOrder, string text);

    [LoggerMessage(LogLevel.Warning,
    "Invalid cached indices for example ID {ExampleId}, segmentation order {SegOrder}, token order {TokenOrder} ({Text}).")]
    partial void LogInvalidCache(int exampleId, int segOrder, int tokenOrder, string text);

    [LoggerMessage(LogLevel.Warning,
    "Unused cache key exists for example ID {ExampleId}, segmentation order {SegOrder}, token order {TokenOrder}.")]
    partial void LogUnusedCacheKey(int exampleId, int segOrder, int tokenOrder);
}
