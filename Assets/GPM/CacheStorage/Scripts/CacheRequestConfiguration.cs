using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gpm.CacheStorage
{
    [Serializable]
    public class CacheRequestConfiguration
    {    
        [SerializeField]
        public CacheRequestType requestType;
        
        [SerializeField]
        public double reRequestTime = 0;
        
        [SerializeField]
        public CacheValidTime validCacheTime = new CacheValidTime();
        
        [SerializeField]
        public Dictionary<string, string> header = new Dictionary<string, string>();

        public CacheRequestConfiguration()
        {
            this.requestType = GpmCacheStorage.GetCacheRequestType();
            this.reRequestTime = GpmCacheStorage.GetReRequestTime();
        }
        
        public CacheRequestConfiguration(CacheRequestType requestType)
        {
            this.requestType = requestType;
            this.reRequestTime = GpmCacheStorage.GetReRequestTime();
        }
        
        public CacheRequestConfiguration(double reRequestTime)
        {
            this.requestType = GpmCacheStorage.GetCacheRequestType();
            this.reRequestTime = reRequestTime;
        }
        
        public CacheRequestConfiguration(CacheValidTime validCacheTime)
        {
            this.requestType = GpmCacheStorage.GetCacheRequestType();
            this.reRequestTime = GpmCacheStorage.GetReRequestTime();
            this.validCacheTime = validCacheTime;
        }
        
        public CacheRequestConfiguration(Dictionary<string, string> header)
        {
            this.requestType = GpmCacheStorage.GetCacheRequestType();
            this.reRequestTime = GpmCacheStorage.GetReRequestTime();
            this.header = header;
        }
        
        public CacheRequestConfiguration(CacheRequestType requestType, double reRequestTime)
        {
            this.requestType = requestType;
            this.reRequestTime = reRequestTime;
        }
        
        public CacheRequestConfiguration(CacheRequestType requestType, CacheValidTime validCacheTime)
        {
            this.requestType = requestType;
            this.reRequestTime = GpmCacheStorage.GetReRequestTime();
            this.validCacheTime = validCacheTime;
        }
        
        public CacheRequestConfiguration(CacheRequestType requestType, Dictionary<string, string> header)
        {
            this.requestType = requestType;
            this.reRequestTime = GpmCacheStorage.GetReRequestTime();
            this.header = header;
        }
        
        public CacheRequestConfiguration(double reRequestTime, CacheValidTime validCacheTime)
        {
            this.requestType = GpmCacheStorage.GetCacheRequestType();
            this.reRequestTime = reRequestTime;
            this.validCacheTime = validCacheTime;
        }
        
        public CacheRequestConfiguration(double reRequestTime, Dictionary<string, string> header)
        {
            this.requestType = GpmCacheStorage.GetCacheRequestType();
            this.reRequestTime = reRequestTime;
            this.header = header;
        }
        
        public CacheRequestConfiguration(CacheRequestType requestType, double reRequestTime, CacheValidTime validCacheTime)
        {
            this.requestType = requestType;
            this.reRequestTime = reRequestTime;
            this.validCacheTime = validCacheTime;
        }
        
        public CacheRequestConfiguration(CacheRequestType requestType, double reRequestTime, Dictionary<string, string> header)
        {
            this.requestType = requestType;
            this.reRequestTime = reRequestTime;
            this.header = header;
        }
        
        public CacheRequestConfiguration(CacheRequestType requestType, double reRequestTime, CacheValidTime validCacheTime, Dictionary<string, string> header)
        {
            this.requestType = requestType;
            this.reRequestTime = reRequestTime;
            this.validCacheTime = validCacheTime;
            this.header = header;
        }
        
        public void Always()
        {
            requestType = CacheRequestType.ALWAYS;
            reRequestTime = 0;
            validCacheTime.min = 0;
            validCacheTime.max = 0;
        }

        public void AddHeader(string key, string value)
        {
            header.Add(key, value);
        }
    }
}