// Copyright (c) Stephen Kraus
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// This file, 02-TokenHighlightService.cs, is part of Jitendex.
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

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jitendex.Data.Home;
using Jitendex.Data.Home.Entities.Tatoeba;

namespace Jitendex.Import.Home.Services.Tatoeba;

internal sealed class TokenHighlightService
(
    HomeContext context,
    ServiceOptions options
) :
    IServiceUnit
{
    public async Task ImportAsync()
    {
        var filePath = GetJsonFilePath();
        await using var stream = File.OpenRead(filePath);
        var data = await JsonSerializer.DeserializeAsync<Dictionary<string, int[]>>(stream) ?? [];

        foreach (var (key, indices) in data)
        {
            var split = key.Split("-");

            var token = new Token
            {
                ExampleId = int.Parse(split[0]),
                SegmentationOrder = int.Parse(split[1]),
                Order = int.Parse(split[2]),
            };

            context.Tokens.Add(token);

            foreach (var index in indices)
            {
                context.TokenHighlights.Add(new TokenHighlight
                {
                    ExampleId = token.ExampleId,
                    SegmentationOrder = token.SegmentationOrder,
                    TokenOrder = token.Order,
                    StartIndex = index,
                    Token = token,
                });
            }
        }

        context.SaveChanges();
    }

    public async Task ExportAsync()
    {
        var query = context.Tokens
            .OrderBy(static t => t.ExampleId)
            .ThenBy(static t => t.SegmentationOrder)
            .ThenBy(static t => t.Order)
            .Select(static t => new
            {
                t.ExampleId,
                t.SegmentationOrder,
                t.Order,
                StartIndices = t.Highlights
                    .OrderBy(static h => h.StartIndex)
                    .Select(static h => h.StartIndex)
            });

        var data = new JsonObject();

        foreach (var token in query)
        {
            var key = $"{token.ExampleId}-{token.SegmentationOrder}-{token.Order}";
            var array = new JsonArray();
            foreach (var index in token.StartIndices)
                array.Add(index);
            data[key] = array;
        }

        var filePath = GetJsonFilePath();
        await using var stream = File.OpenWrite(filePath);
        JsonSerializer.Serialize(stream, data, JsonSerializerOptions);
        stream.Write("\n"u8);
    }

    private string GetJsonFilePath()
        => Path.Join
        (
            options.GetDirectory(DataDirectory.Tatoeba).FullName,
            "ambiguous_token_highlight_indices.json"
        );

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        IndentSize = 4,
        Encoder = JavaScriptEncoder.Default,
    };
}
