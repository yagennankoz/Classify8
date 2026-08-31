using System;
using System.IO;
using Xunit;
using Classify8.Core;

namespace Classify8.Tests
{
    public class DynamicPathBuilderTests
    {
        [Fact]
        public void パス動的生成テスト_指定したタグが正しく置換されるか()
        {
            var rule = new SortRule 
            { 
                RuleName = "テストルール", 
                SearchCondition = "*.pdf /& 請求" 
            };
            
            // テスト用のベースフォルダ名（タグを含む）
            string templateDir = @"C:\Dest\/YYYY\/NAME\/COND";
            string sourceFilePath = "dummy.pdf"; // 今回のテストでは存在しないファイルでもOK

            string result = DynamicPathBuilder.BuildPath(templateDir, sourceFilePath, rule);

            // 期待値: 
            // /YYYY -> 今年の年(yyyy)
            // /NAME -> テストルール
            // /COND -> *.pdf & 請求  (※スラッシュが除去される)
            string expectedYear = DateTime.Now.ToString("yyyy");
            string expectedResult = $@"C:\Dest\{expectedYear}\テストルール\*.pdf & 請求";

            Assert.Equal(expectedResult, result);
        }
    }
}