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
                // TODO: Fetch and validate correct indices from cache.
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

        table.InsertItems(tatoebaContext, rows);
    }

    [LoggerMessage(LogLevel.Warning,
    "Example ID {ExampleId} does not contain expected text `{Text}`.")]
    partial void LogMissingSentenceText(int exampleId, string text);
}
