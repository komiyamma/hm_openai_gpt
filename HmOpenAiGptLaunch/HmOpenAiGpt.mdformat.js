/*
 * Copyright (c) 2024 Akitsugu Komiyama
 * under the MIT License
 */

function formatMarkdownTable(text) {
    // 正規表現を使ってマークダウン形式のテーブルを検索
    const tableRegex = /(\|.*(?:\|.*\|.*\n)+)+/g;

    function replaceTable(match) {
        try {
            // テーブル全体を行に分割。無意味な行はカット
            const lines = match.split("\n")
                             .filter(line => !/^\s*$/.test(line));

            // 各行を「|」で分割し、セル内の文字列を確保する
            // 各行を「|」で分割し、セル内の文字列を確保する
            let cells_in_line = lines.map(line => {
                // 行を「|」で分割
                let cells = line.split("|");
                // 最低でも||と先頭と末尾を意味する数が必要なので、３つに分割されるはず。
                if (cells.length >= 3) {
                    // 先頭と末尾は要素をカット。要素内はtrim
                    cells = cells.slice(1, -1).map(cell => cell.trim());
                }
                return cells;
            });

            // それぞれのセルの長さ情報を確保する
            const cells_len_in_line = cells_in_line.map(
                                        cells => cells.map(cell => getCellLength(cell))
                                      );

            // それぞれの列の最大の文字数
            let cells_maxlen = [];

            cells_len_in_line.forEach(maxs => {
                maxs.forEach((len, c) => {
                    // 最大文字数未定義または現在の文字数がより大きい場合に更新
                    cells_maxlen[c] = Math.max(len, cells_maxlen[c] ?? 0);
                });
            });

            // 上下のカラムを見て、最大の数で修正する
            cells_in_line.forEach((cells, r) => {
                cells.forEach((cell, c) => {

                    // そのセルの上下セルまでみた最大の文字数から、そのセルの文字数を引いた数。これが追加の空白が必要な数
                    let need_spacecount = cells_maxlen[c] - cells_len_in_line[r][c];

                    // その長さに合わせて加工する
                    cells[c] = addSpaces(cell, need_spacecount);
                });
            });

            // 分解時の逆。「|」で繋げて、先頭と末尾にも「|」をくっつける。
            let formated_lines = cells_in_line.map(cells => "|" + cells.join("|") + "|");

            // 同様の「\n」での連結だが、テーブルの安全のため、必ず先頭と末尾に改行をくっつけておく。
            let join_all = "\n" + formated_lines.join("\n") + "\n";

            return join_all;
        } catch(err) {
        }
        return match;
    }

    // プロポーショナル
    function getCellLength(text) {
        let length = 0;
        for (const char of text) {
            length += (char.codePointAt(0) <= 255) ? 1 : 2;
        }
        return length;
    }

    function addSpaces(text, n) {
        return text + ' '.repeat(n);
    }

    // テキスト内のすべてのテーブルを「整形」することを試みる
    return text.replace(tableRegex, replaceTable);
}

