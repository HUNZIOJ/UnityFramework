using UnityEngine;

namespace Frame.YooAsset
{
    public sealed class YooAssetResourceUpdateOptions
    {
        public string PackageName { get; set; }

        public string[] Tags { get; set; }

        public bool AppendTimeTicks { get; set; } = true;

        public int TimeoutSeconds { get; set; } = 60;

        public int DownloadMaxConcurrency { get; set; } = 5;

        public int DownloadRetryCount { get; set; } = 3;

        public bool ClearUnusedCacheAfterUpdate { get; set; }

        public YooAssetResourceUpdateOptions Normalize(string defaultPackageName, int defaultMaxConcurrency)
        {
            YooAssetResourceUpdateOptions copy = new YooAssetResourceUpdateOptions
            {
                PackageName = string.IsNullOrWhiteSpace(PackageName) ? defaultPackageName : PackageName.Trim(),
                Tags = NormalizeTags(Tags),
                AppendTimeTicks = AppendTimeTicks,
                TimeoutSeconds = Mathf.Max(1, TimeoutSeconds),
                DownloadMaxConcurrency = Mathf.Max(1, DownloadMaxConcurrency <= 0 ? defaultMaxConcurrency : DownloadMaxConcurrency),
                DownloadRetryCount = Mathf.Max(0, DownloadRetryCount),
                ClearUnusedCacheAfterUpdate = ClearUnusedCacheAfterUpdate
            };

            return copy;
        }

        private static string[] NormalizeTags(string[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                return null;
            }

            int count = 0;
            string[] normalized = new string[tags.Length];
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i];
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                normalized[count] = tag.Trim();
                count++;
            }

            if (count == 0)
            {
                return null;
            }

            if (count == normalized.Length)
            {
                return normalized;
            }

            string[] compact = new string[count];
            for (int i = 0; i < count; i++)
            {
                compact[i] = normalized[i];
            }

            return compact;
        }
    }
}
