using System;
using UnityEngine;

namespace Gpm.CacheStorage
{
    [Serializable]
    public class CacheValidTime
    {
        [SerializeField]
        public double min = 0;
        
        [SerializeField]
        public double max = 0;

        public CacheValidTime()
        {
            
        }
        
        public CacheValidTime(double min)
        {
            this.min = min;
        }
        
        public CacheValidTime(double min, double max)
        {
            this.min = min;
            this.max = max;
        }
    }
}