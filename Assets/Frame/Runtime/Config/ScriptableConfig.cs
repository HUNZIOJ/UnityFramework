using UnityEngine;

namespace Frame.Config
{
    public abstract class ScriptableConfig : ScriptableObject
    {
        [SerializeField] private string id = "";

        public string Id
        {
            get { return string.IsNullOrWhiteSpace(id) ? name : id; }
        }
    }
}
