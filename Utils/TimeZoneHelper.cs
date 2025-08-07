using System;

namespace AzureRag.Utils
{
    public static class TimeZoneHelper
    {
        /// <summary>
        /// 日本のタイムゾーン（JST）を取得します
        /// </summary>
        public static TimeZoneInfo GetJapanTimeZone()
        {
            TimeZoneInfo jstTimeZone;
            try 
            {
                jstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
            } 
            catch 
            {
                try 
                {
                    // Windowsの場合は別のID
                    jstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                }
                catch
                {
                    // タイムゾーンが見つからない場合は9時間オフセットを使用
                    jstTimeZone = TimeZoneInfo.CreateCustomTimeZone("JST", TimeSpan.FromHours(9), "JST", "Japan Standard Time");
                }
            }
            return jstTimeZone;
        }

        /// <summary>
        /// 現在のUTC時間を日本時間（JST）に変換して返します
        /// </summary>
        public static DateTime GetCurrentJapanTime()
        {
            var jstTimeZone = GetJapanTimeZone();
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, jstTimeZone);
        }

        /// <summary>
        /// 日本時間（JST）でフォーマットされた現在のタイムスタンプを返します (yyyyMMdd-HHmmss)
        /// </summary>
        public static string GetCurrentJapanTimeStamp()
        {
            return GetCurrentJapanTime().ToString("yyyyMMdd-HHmmss");
        }
    }
}