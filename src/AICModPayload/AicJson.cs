using System.Collections.Generic;
using System.Text;
using AICShared;

namespace AICMod
{
    /// <summary>
    /// 轻量 JSON 序列化辅助：Unity 的 JsonUtility 实测无法序列化 List&lt;自定义类&gt; 字段（会返回空对象 {}），
    /// 地图/物品列表统一改用这里手工构造标准 JSON，客户端 System.Text.Json 可直接解析。
    /// </summary>
    internal static class AicJson
    {
        private static string Str(string? s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string ItemList(IList<ItemEntryDto> items)
        {
            var sb = new StringBuilder();
            sb.Append("{\"Items\":[");
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var it = items[i];
                    sb.Append("{\"Key\":").Append(Str(it.Key))
                      .Append(",\"Name\":").Append(Str(it.Name))
                      .Append(",\"Category\":").Append(Str(it.Category))
                      .Append(",\"CategoryZh\":").Append(Str(it.CategoryZh))
                      .Append(",\"OwnedCount\":").Append(it.OwnedCount)
                      .Append('}');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static string MapList(IList<MapEntryDto> maps)
        {
            var sb = new StringBuilder();
            sb.Append("{\"Maps\":[");
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var m = maps[i];
                    sb.Append("{\"Key\":").Append(Str(m.Key))
                      .Append(",\"Name\":").Append(Str(m.Name))
                      .Append(",\"AreaKey\":").Append(Str(m.AreaKey))
                      .Append(",\"AreaName\":").Append(Str(m.AreaName))
                      .Append('}');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static string EffectList(IList<EffectEntryDto> effects)
        {
            var sb = new StringBuilder();
            sb.Append("{\"Effects\":[");
            if (effects != null)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var e = effects[i];
                    sb.Append("{\"Id\":").Append(e.Id)
                      .Append(",\"Key\":").Append(Str(e.Key))
                      .Append(",\"Name\":").Append(Str(e.Name))
                      .Append(",\"MaxLevel\":").Append(e.MaxLevel)
                      .Append('}');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
