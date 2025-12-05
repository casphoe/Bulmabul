using System.Collections.Generic;

namespace Gpm.Ui
{
    using System;
    using UnityEngine;
    using UnityEngine.UI;
    using UnityEngine.Events;
    using Gpm.CacheStorage;
    using Gpm.CacheStorage.Internal;

    [RequireComponent(typeof(RawImage))]
    public class WebCacheImage : MonoBehaviour
    {
        [SerializeField]
        private string url;
       
        [SerializeField]
        private bool preLoad = true;
        
        [SerializeField]
        private CacheRequestConfiguration cacheConfig = new CacheRequestConfiguration(
            CacheRequestType.ONCE, 
            60 * 60 * 2, 
            new CacheValidTime(60 * 5, 60 * 60 * 24 * 30)); 

        [SerializeField]
        private LoadTextureEvent onLoadTexture = new LoadTextureEvent();

        private RawImage image;

        private CacheRequestOperation operation;

        private bool isInitilize = false;

        public RawImage Image
        {
            get
            {
                if (image == null)
                {
                    image = GetComponent<RawImage>();
                }

                return image;
            }

            private set
            {
                image = value;
            }
        }
        
        public CacheRequestOperation Operation
        {
            get
            {
                return operation;
            }
        }
        

        public CacheInfo CacheInfo
        {
            get
            {
                return operation;
            }
        }

        private void Awake()
        {
            CacheStorageInternal.Initialize();

            if (image == null)
            {
                image = GetComponent<RawImage>();
            }
        }
        private void OnEnable()
        {
            if(isInitilize == false)
            {
                if(preLoad == true)
                {
                    Preload();
                }

                isInitilize = true;
            }
        }

        public void Preload()
        {
            if (image != null)
            {
                operation = GpmCacheStorage.GetCachedTexture(url, (cachedTexture) =>
                {
                    if (cachedTexture != null)
                    {
                        Image.texture = cachedTexture.texture;
                    }
                });
            }
        }

        public void LoadImage()
        {
            if (image != null)
            {
                Image.texture = null;

                if (string.IsNullOrEmpty(this.url) == false)
                {
                    operation = GpmCacheStorage.RequestTexture(url, cacheConfig, preLoad, (cachedTexture) =>
                    {
                        if (cachedTexture != null)
                        {
                            Image.texture = cachedTexture.texture;
                        }
                    });
                }
            }
        }

        public void SetUrl(string url)
        {
            if(this.url != url)
            {
                this.url = url;

                if (operation != null)
                {
                    operation.Cancel();
                    operation = null;
                }
                
                LoadImage();
            }
        }
        
        public void SetUrl(string url, Dictionary<string, string> header)
        {
            SetHeader(header);
            SetUrl(url);
        }

        public void SetLoadTextureEvent(UnityAction<Texture> onListener)
        {
            CleatLoadTextureEvent();
            AddLoadTextureEvent(onListener);
        }
        
        public void AddLoadTextureEvent(UnityAction<Texture> onListener)
        {
            onLoadTexture.AddListener(onListener);
        }
        
        public void CleatLoadTextureEvent()
        {
            onLoadTexture = new LoadTextureEvent();
        }
        
        public void SetPreloadSetting(bool preLoad)
        {
            this.preLoad = preLoad;
        }
        
        public void SetHeader(Dictionary<string, string> header)
        {
            cacheConfig.header = header;
        }
        
        public void SetCacheConfig(CacheRequestConfiguration config)
        {
            cacheConfig = config;
        }
        
        public void SetCacheRequestType(CacheRequestType requestType)
        {
            cacheConfig.requestType = requestType;
        }
        
        public void SetCacheReRequestTime(double reRequestTime)
        {
            cacheConfig.reRequestTime = reRequestTime;
        }
        
        public void SetCacheValidMinTime(double min)
        {
            cacheConfig.validCacheTime.min = min;
        }
        
        public void SetCacheValidMaxTime(double max)
        {
            cacheConfig.validCacheTime.max = max;
        }
        
        public void SetCacheValidTime(double min, double max)
        {
            cacheConfig.validCacheTime.min = min;
            cacheConfig.validCacheTime.max = max;
        }
        
        public string GetURL()
        {
            return url;
        }
        
        public bool GetPreloadSetting() 
        { 
            return preLoad; 
        }
        
        public CacheRequestType GetCacheRequestType()
        {
            return cacheConfig.requestType;
        }
        
        public double GetCacheReRequestTime()
        {
            return cacheConfig.reRequestTime;
        }
        
        public double GetCacheValidMinTime()
        {
            return cacheConfig.validCacheTime.min;
        }
        
        public double GetCacheValidMaxTime()
        {
            return cacheConfig.validCacheTime.max;
        }

        [Serializable]
        public class LoadTextureEvent : UnityEvent<Texture>
        {
            public LoadTextureEvent()
            {
            }
        }
    }
}