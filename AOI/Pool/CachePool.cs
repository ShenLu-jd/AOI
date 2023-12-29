using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOI
{
    public class CachePool<T> where T : new()
    {
        Queue<T> objs = new();
        public int Num => objs.Count;
        
        public CachePool()
        {

        }

        public T Get()
        {
            if(objs.Count == 0)
            {
                return CreateObj();
            }
            
            return objs.Dequeue();
        }

        private T CreateObj()
        {
            return new T();
        }

        public void Recycle(T obj)
        {
            objs.Enqueue(obj);
        }
    }
}
