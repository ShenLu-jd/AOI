using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOI
{
    class GridMap
    {
        // 格子地图最大的格子数量
        public const int MAX_MAP_GRID = 256;

        private int _id;
        // 现在的游戏都是精确到像素级别的
        // 但是地形用grid的话没法精确到像素级别，也没必要。因此后端只记录格子坐标
        // 一个格子可以表示64*64像素大小，或者32*32，具体看策划要求
        // 65535*32 = 2097120像素，一般情况下，uint16_t足够大了
        private int _width;     // 地图的宽，格子坐标
        private int _height;    // 地图的长度，格子坐标
        private int[] _gridSet; // 格子数据集合

        public bool SetGridMap(int id, int width, int height)
        {
            if (width > MAX_MAP_GRID || height >= MAX_MAP_GRID) return false;

            _id = id;
            _width = width;
            _height = height;
            _gridSet = new int[width * height];
            return true;
        }

        // 填充地图信息
        public bool FillGridMap(int x, int y, int  cost)
        {
            if (x >= _width || y >= _height) return false;

            _gridSet[x * _height + y] = cost;

            return true;
        }

    }
}
