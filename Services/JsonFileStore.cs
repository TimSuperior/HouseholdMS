using System.IO;
using Newtonsoft.Json;

namespace HouseholdMS.Services
{
    public static class JsonFileStore
    {
        public static void Save<T>(string path, T obj)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented));
        }
        public static T Load<T>(string path)
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }
    }
}
