/*
 * Copyright (c) 2024 Akitsugu Komiyama
 * under the MIT License
 */


function formatMarkdownTable(text) {
    // 正規表現を使ってマークダウン形式のテーブルを検索するパターン
    const tableRegex = /(\|.*(?:\|.*\|.*\n)+)+/g;

    function replaceTable(match) {
        try {
            // テーブル全体を行に分割
            let lines = match.split("\n");
            lines = lines.filter(line => !/^\s*$/.test(line));

            // 各行を「|」で分割し、セル内の文字列を確保する
            let cells_in_line = [];
            for(let r=0; r<lines.length; r++) {
                let line = lines[r];
                // 行を「|」で分割
                let cells = line.split("|");
                // 最低でも||と先頭と末尾を意味する数が必要なので、３つに分割されるはず。
                if (cells.length >= 3) {
                    // 先頭と末尾はカット
                    cells = cells.slice(1, cells.length - 1);
                    // 各セルで文字列の前後の空白はカット
                    cells = cells.map(cell => cell.trim());
                }
                cells_in_line[r] = cells;
            }
            
            // それぞれのセルの長さ情報を確保する
            let cells_len_in_line = [];
            for(let r=0; r<cells_in_line.length; r++) {
                // １行のセルリスト
                let cells = cells_in_line[r];
                // 対応する長さリストを用意
                cells_len_in_line.push([]);
                for(let c=0; c<cells.length; c++) {
                    // 半角英数記号は１文字、それ以外は２文字としてカウント。
                    let textlen = getCellLength(cells[c]);
                    // 文字列の長さに応じて、埋めていく
                    cells_len_in_line[r].push(textlen);
                }
            }

            // それぞれの列の最大の文字数
            let cells_maxlen = [0,0,0,0,0,0,0,0,0,0];
            // カラム数のcolは10まで対応
            for(let c=0; c<cells_maxlen.length; c++) {
                // 上下のカラムを見て、最大の数で修正する
                for(let r=0; r<cells_len_in_line.length; r++) {
                    let lens = cells_len_in_line[r];
                    // テーブルが不正確で要素が無いかもしれない。
                    if (c < lens.length) {
                        // その行のその列の文字列の長さが、cells_maxlenの対応する列の長さより大きいなら、
                        if (lens[c] > cells_maxlen[c] ) {
                            cells_maxlen[c] = lens[c];
                        } 
                    }
                }
            }

            // 上下のカラムを見て、最大の数で修正する
            for(let r=0; r<cells_in_line.length; r++) {
                let cells = cells_in_line[r];
                // 最大でも10列までの処理
                for(let c=0; c<cells.length && cells_maxlen.length; c++) {
                    // そのセルのの最大の長さ
                    let raw_maxlen = cells_maxlen[c];
                    // そのセルの現在の長さ
                    let raw_curlen = cells_len_in_line[r][c];
                    // 長さを最大に合わせるために必要となる空白数
                    let need_spacecount = raw_maxlen-raw_curlen;

                    // その長さに合わせて加工する
                    cells[c] = addSpaces(cells[c], need_spacecount);
                }
            }

            let result_list = [];

            for(let r=0; r<cells_in_line.length; r++) {
                let cells = cells_in_line[r];
                let jointext = cells.join("|");
                jointext = "|" + jointext + "|";
                result_list.push(jointext);
            }

            let join_all = "\n" + result_list.join("\n") + "\n";
            // それぞれの列の最大の文字数に合わせてpadspaceする

            return join_all;
        } catch(err) {
        }
        return match;
    }

    function getCellLength(text) {
        let length = 0;
        for (const char of text) {
            if (char.codePointAt(0) <= 255) {
                length += 1;
            } else {
                length += 2;
            }
        }
        return length;
    }

    function addSpaces(text, n) {
        for (let i = 0; i < n; i++) {
            text += ' ';
        }
        return text;
    }

    // テキスト内のすべてのテーブルを「整形」することを試みる
    return text.replace(tableRegex, replaceTable);
}


