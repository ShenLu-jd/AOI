using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOI
{
    using Entity = GridAOI.Entity;

    class GridTest
    {
        int mapWidth = 10, mapHeight = 10; // 格子的行数、列数
        int visualWidth = 1, visualHeight = 1;  // 左右、上下能看到几个格子的视野
        int pixGrid = 100;  // 每个格子的大小 100*100

        //int mapWidth = 100, mapHeight = 100; // 格子的行数、列数
        //int visualWidth = 1, visualHeight = 1;  // 左右、上下能看到几个格子的视野
        //int pixGrid = 100;  // 每个格子的大小 100*100

        GridAOI gridAOI;
        int maxId = 0;

        public GridTest()
        {
            // 初始化AOI格子
            gridAOI = new GridAOI(mapWidth * pixGrid, mapHeight * pixGrid,
                                    visualWidth * pixGrid, visualHeight * pixGrid, pixGrid);
            // 为每个格子里面添加一个Entity,每个Entity的视野XY的范围为(visualWidth,visualHeight)
            for (int y = 0; y < mapWidth; y++)
            {
                for (int x = 0; x < mapHeight; x++)
                {
                    gridAOI.EnterEntity(maxId++, x * pixGrid + pixGrid / 2, y * pixGrid + pixGrid / 2, 1);
                }
            }

            Console.WriteLine("初始化");
            gridAOI.PrintGrid();
            Console.WriteLine("-------------------------------------------------");

            Test();
            //TestRandom();
        }

        public void Test()
        {
            // 移动像素个单位

            // 第4行，第5个，向右移动2个格子（pixGrid为100，200就是2个格子）
            UpdateEntity(35, 200, 0);
            // 第8行，第4个，向右移动1个格子，向下移动1个格子
            UpdateEntity(73, 100, 100);
            // 第1行，第2个，向下移动3个格子
            UpdateEntity(1, 0, 300);
            // 第2行，第4个，向上移动1个格子
            UpdateEntity(13, 0, -100);
        }

        // 随机测试
        public void TestRandom()
        {
            Random random = new();
            List<Entity> listIn = new();
            List<Entity> listOut = new();
            for (int i=0; i<10000; i++)
            {
                int id = random.Next(0, maxId);
                var entity = gridAOI.GetEntity(id);
                int newX = random.Next(0, mapWidth * pixGrid);
                int nweY = random.Next(0, mapHeight * pixGrid);

                listIn.Clear();
                listOut.Clear();
                gridAOI.UpdateEntity(id, newX, nweY, listIn, listOut);
                //gridAOI.PrintGrid();
                //Console.WriteLine("-------------------------------------------------");
            }
        }

        /// <summary>
        /// 测试移动代码
        /// </summary>
        /// <param name="id">Entity的ID</param>
        /// <param name="offsetX">X方向移动的像素距离</param>
        /// <param name="offsetY">Y方向移动的像素距离</param>
        private void UpdateEntity(int id, int offsetX, int offsetY)
        {
            var entity = gridAOI.GetEntity(id);

            List<Entity> listIn = new();
            List<Entity> listOut = new();

            // 移动前entity的信息
            Console.WriteLine($"id = {id}, posX = {entity.posX}, posY = {entity.posY}");
            PrintList(entity.interestMe, "interestMe");

            // Entity移动
            gridAOI.UpdateEntity(id, entity.posX * pixGrid + offsetX, entity.posY * pixGrid + offsetY, listIn, listOut);
            Console.WriteLine("移动");

            // 移动后entity的信息
            Console.WriteLine($"id = {id}, posX = {entity.posX}, posY = {entity.posY}");
            PrintList(entity.interestMe, "interestMe");

            // 打印当前格子
            gridAOI.PrintGrid();
            Console.WriteLine();

            // 打印加入Entity和退出Entity的其他Entity
            Console.WriteLine("entity 附近新加入的其他entity");
            PrintList(listIn, "ListIn");
            Console.WriteLine("entity 退出的其他entity");
            PrintList(listOut, "ListOut");

            Console.WriteLine("-------------------------------------------------");
        }

        void PrintList(List<Entity> list, string name = "")
        {
            if (list == null || list.Count == 0)
            {
                Console.WriteLine($"{name} 为空");
                return;
            }

            Console.WriteLine($"{name} 长度为 ： {list.Count}");
            for (int i=0; i< list.Count; i++)
            {
                Console.Write($"{list[i].id} ");
            }
            Console.WriteLine();
        }
    }
}
