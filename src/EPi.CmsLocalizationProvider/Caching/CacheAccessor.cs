using System;
using EPiServer.Framework.Cache;
using EPiServer.ServiceLocation;

namespace EPi.CmsLocalizationProvider.Caching
{
    public class CacheAccessor<TCache> : IDisposable where TCache : IIsDirty
    {
        private readonly string _key;
        private readonly bool _readonly;
        private Injected<ISynchronizedObjectInstanceCache> _cache { get; set; }
        public TCache Cache { get; set; }

        public CacheAccessor(string key, bool @readonly = false)
        {
            _key = key;
            _readonly = @readonly;

            Cache = GetCache();
        }

        private TCache GetCache()
        {
            var cache = (TCache) _cache.Service.Get(_key);

            if (cache == null)
                cache = Activator.CreateInstance<TCache>();

            return cache;
        }

        public void Dispose()
        {
            if (_readonly || !Cache.IsDirty) return;

            _cache.Service.Remove(_key);
            _cache.Service.Insert(_key, Cache, CacheEvictionPolicy.Empty);
        }
    }

    public class LocalizationCacheAccessor : CacheAccessor<LocalizationCache>
    {
        public LocalizationCacheAccessor(bool @readonly = false) : base("EPi.CmsLocalizationProvider.Caching.LocalizationCache", @readonly)
        {
        }
    }
}
