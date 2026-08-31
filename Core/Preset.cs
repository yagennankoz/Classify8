using System;

namespace Classify8.Core
{
    public class Preset
    {
        // 内部で一意に識別するためのID (新規作成時は自動でGUIDを割り当て)
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        // ユーザーがGUIで識別しやすい名前 (例: "メイン画像フォルダ")
        public string AliasName { get; set; }
        
        // 実際のディレクトリパス (例: "D:\Backup\Images")
        public string DirectoryPath { get; set; }
    }
}