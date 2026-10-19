Classify8 - 言語ファイルの追加方法 / How to add a language
====================================================================

[日本語]
言語ファイルの置き場所は、Classify8.exe と同じ場所にある「Languages」フォルダ (この README.txt があるフォルダ) です。
ここに「言語コード.json」を置くと、オプション > 表示 の言語一覧に追加されます。
 1. このフォルダの _template.json をコピーして、言語コードをファイル名にします。 例: fr.json, de.json, zh-CN.json
 2. "@name" に、言語一覧に表示する名前(その言語での表記)を書きます。 例: "Français"
 3. 各キーの値を翻訳します。キーは変更しないでください。{0} {1} は、実行時に値が埋め込まれる場所です。残してください。
 4. アプリを再起動し、オプション > 表示 で言語を選びます。
 * 日本語と英語はアプリに内蔵されているため、このフォルダにファイルが無くても使えます。
 * ja.json / en.json をこのフォルダに置くと、書かれているキーだけ、内蔵の文字列を上書きできます。
 * 翻訳していないキーは、日本語の表示になります。
 * 「_」で始まるファイル名 (_template.json など) は読み込まれません。
 * ファイルは UTF-8 (BOMなし) で保存してください。// コメントも書けます。

[English]
Language files go in the "Languages" folder next to Classify8.exe (the folder that contains this README.txt).
Put a "<language code>.json" file here to add a language to the list in Options > Display.
 1. Copy _template.json in this folder and name it with the language code. e.g. fr.json, de.json, zh-CN.json
 2. Set "@name" to the name shown in the language list (written in that language). e.g. "Français"
 3. Translate the value of each key. Do not change the keys. Keep {0} {1}: values are inserted there at runtime.
 4. Restart the app and choose the language in Options > Display.
 * Japanese and English are built into the app, so they work even if this folder has no files for them.
 * Placing ja.json / en.json in this folder overrides only the built-in strings for the keys they contain.
 * Keys you do not translate are shown in Japanese (the base language).
 * Files whose names start with "_" (such as _template.json) are ignored.
 * Save the file as UTF-8 (without BOM). // comments are allowed.
