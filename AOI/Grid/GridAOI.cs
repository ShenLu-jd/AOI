using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOI
{
    /**
     * 格子AOI(Area of Interest)算法
     *
     * 1. 暂定所有实体视野一样
     * 2. 按mmoarpg设计，需要频繁广播技能、扣血等，因此需要interest_me列表
     * 3. 只提供获取矩形范围内实体。技能寻敌时如果不是矩形，上层再从这些实体筛选
     * 4. 通过mask来控制实体之间的交互
     * 5. 假设1m为一个格子，则1平方千米的地图存实体指针的内存为 1024 * 1024 * 8 = 8M
     *    (内存占用有点大，但一个服的地图数量有限，应该是可接受的)
     * 6. 所有对外接口均为像素，内部则转换为格子坐标来处理，但不会记录原有的像素坐标
     */
    public class GridAOI
    {
        /// <summary>
        /// 掩码，按位表示，第一位表示是否加入其他实体interest列表，其他由上层定义
        /// </summary>
        public static byte INTEREST = 0x1;

        /// <summary>
        /// 场景中单个实体的类型、坐标等数据
        /// </summary>
        public class Entity
        {
            /// <summary>
            /// 掩码，按位表示，第一位表示是否加入其他实体interest列表，其他由上层定义
            /// </summary>
            public byte mask;

            public int id;
            public int posX; // 格子坐标X
            public int posY; // 格子坐标Y

            /// <summary>
            /// 对我感兴趣的实体列表。比如我周围的玩家，需要看到我移动、放技能，都需要频繁广播给他们，
            /// 可以直接从这个列表取。如果游戏并不是arpg，可能并不需要这个列表。
            /// 当两个实体的mask存在交集时，将同时放入双方的_interest_me，但这只是做初步的筛选，例
            /// 如不要把怪物放到玩家的_interest_me，但并不能处理玩家隐身等各种复杂的逻辑，还需要上层
            /// 根据业务处理
            /// </summary>
            public List<Entity> interestMe;
        }

        #region 成员变量

        protected int width = 0;    // 场景最大宽度(格子坐标)
        protected int height = 0;   // 场景最大高度(格子坐标)
        protected int pixGrid;      // 每个格子表示的像素大小

        // 格子数指以实体为中心，不包含当前格子，上下或者左右的格子数，宽高都为0代表只能看到当前格子内
        protected int visualWidth = 0;     // 视野宽度格子数
        protected int visualHeight = 0;    // 视野高度格子数


        private CachePool<List<Entity>> entityListPool;
        protected CachePool<List<Entity>> EntityListPool
        {
            get
            {
                if(entityListPool == null)
                {
                    return new();
                }
                return entityListPool;
            }
        }


        private CachePool<Entity> entityPool;
        protected CachePool<Entity> EntityPool
        {
            get
            {
                if (entityPool == null)
                {
                    entityPool = new();
                }
                return entityPool;
            }
        }

        /// <summary>
        /// 记录每个格子中的实体id列表
        /// 有X[m][n]、X[_width * m + n]这两种存储方式，第二种效率更高
        /// 一个地图中，绝大部分格子是不可行走的，因此对应的格子需要用到才会创建数组，节省内存
        /// </summary>
        protected List<Entity>[] entityGrid;

        // 记录所有实体的数据
        protected Dictionary<int, Entity> entityDict = new();
        
        #endregion


        public GridAOI()
        {

        }

        public GridAOI(int mapWidth, int mapHeight, int visualWidth, int visualHeight, int pixGrid)
        {
            SetSize(mapWidth, mapHeight, pixGrid);
            SetVisualRange(visualWidth, visualHeight);
        }

        // 设置 AOI 格子范围
        public void SetSize(int width, int height, int pixGrid)
        {
            if (pixGrid <= 0)
            {
                Console.WriteLine($"非法的pixGrid值：{pixGrid}");
                return;
            }

            this.pixGrid = pixGrid;
            this.width = (int)Math.Ceiling((double)width / pixGrid);
            this.height = (int)Math.Ceiling((double)height / pixGrid);

            entityGrid = new List<Entity>[this.width * this.height];
        }

        // 设置视野，必须先设置场景大小后才能调用此函数
        public bool SetVisualRange(int width, int height)
        {
            if (pixGrid <= 0) return false;

            visualWidth = (int)Math.Ceiling((double)width / pixGrid);
            visualHeight = (int)Math.Ceiling((double)height / pixGrid);
            return true;
        }

        /// <summary>
        /// 判断两个位置视野交集
        /// </summary>
        /// <param name="posX">实体旧位置坐标X</param>
        /// <param name="posY">实体旧位置坐标Y</param>
        /// <returns> x,y,dx,dy:矩形区域视野的对角坐标 </returns>
        protected (int x, int y, int dx, int dy) GetVisualRange(int posX, int posY)
        {
            // 以pos为中心，构造一个矩形视野 (x,y) 左上角 (dx,dy) 右下角
            int x = posX - visualWidth;
            int y = posY - visualHeight;
            int dx = posX + visualWidth;
            int dy = posY + visualHeight;

            // 处理边界
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (dx >= width) dx = width - 1;
            if (dy >= height) dy = height - 1;
            return (x, y, dx, dy);
        }

        #region 对象池相关
        // 回收一个 EntityList
        protected void DelEntityList(List<Entity> list)
        {
            if (list == null) return;

            // 太大的直接删除不要缓存，避免缓存消耗太多内存
            if (list.Count > 128)
                list.Clear();
            EntityListPool.Recycle(list);
        }

        // 获取一个 EntityList
        protected List<Entity> NewEntityList()
        {
            List<Entity> entityList = EntityListPool.Get();
            entityList.Clear();
            return entityList;
        }

        // 回收一个 Enitty
        protected void DelEntity(Entity entity)
        {
            DelEntityList(entity.interestMe);
            EntityPool.Recycle(entity);
        }

        // 获取一个 Entity
        protected Entity NewEntity()
        {
            Entity entity = EntityPool.Get();
            entity.interestMe = NewEntityList();
            return entity;
        }
        #endregion

        /// <summary>
        /// 遍历矩形内的实体(坐标为像素坐标)
        /// </summary>
        /// <param name="x">物体像素坐标X</param>
        /// <param name="y">物体像素坐标Y</param>
        protected bool EachRangeEntity(int x, int y, int dx, int dy, Action<Entity> func)
        {
            // 4个坐标必须为矩形的对角像素坐标,这里转换为左上角和右下角坐标
            if (x > dx || y > dy)
            {
                Console.WriteLine($"非法坐标 ({x},{y}) ({dx},{dy})");
                return false;
            }

            // 转换为格子坐标
            x = x / pixGrid;
            y = y / pixGrid;
            dx = dx / pixGrid;
            dy = dy / pixGrid;

            if (!ValidPos(x, y, dx, dy)) return false;

            RawEachRangeEntity(x, y, dx, dy, func);
            return true;
        }

        /// <summary>
        /// 遍历矩形内的实体，不检测范围(坐标为格子坐标)
        /// </summary>
        protected void RawEachRangeEntity(int x, int y, int dx, int dy, Action<Entity> func)
        {
            // 遍历范围内的所有格子
            // 注意坐标是格子的中心坐标，因为要包含当前格子，用<=
            for (int i = x; i <= dx; i++)
            {
                for (int j = y; j <= dy; j++)
                {
                    List<Entity> list = entityGrid[i + width * j];
                    if(list != null)
                    {
                        foreach (Entity entity in list)
                        {
                            func(entity);
                        }
                    }
                }
            }
        }

        // 从列表中删除一个指定实体
        private static bool RemoveEntityFromList(List<Entity> list, Entity entity)
        {
            if (list == null) return false;

            for (int i=0; i < list.Count; i++)
            {
                Entity item = list[i];
                if (item == entity)
                {
                    // 用最后一个元素替换就好，不用移动其他元素
                    list[i] = list.Last();
                    list[list.Count - 1] = item;
                    list.RemoveAt(list.Count-1);
                    return true;
                }
            }
            return false;
        }

        // 插入实体到格子内
        private void InsertGridEntity(int x, int y, Entity entity)
        {
            List<Entity> list = GetListInEntityGrid(x, y);
            if(list == null)
            {
                list = NewEntityList();
                SetListInEntityGrid(x, y, list);
            }
            list.Add(entity);
        }

        // 从格子内删除实体
        private bool RemoveGridEntity(int x, int y, Entity entity)
        {
            // 当列表为空时，把数组从 entity_grid 移除放到池里复用
            List<Entity> list = entityGrid[x + width * y];
            
            if (list == null) return false;

            RemoveEntityFromList(list, entity);

            if(list.Count == 0)
            {
                entityGrid[x + width * y] = null;
                DelEntityList(list);
            }

            return true;
        }

        // 处理实体进入场景
        /// <param name="x">物体像素坐标X</param>
        /// <param name="y">物体像素坐标Y</param>
        public int EnterEntity(int id, int x, int y, byte mask, List<Entity> list = null)
        {
            // 检测坐标
            int gx = x / pixGrid;
            int gy = y / pixGrid;
            if (!ValidPos(gx, gy, gx, gy)) return -1;

            // 防止重复进入场景
            bool hasEntity = entityDict.TryGetValue(id, out Entity ret);
            if (hasEntity) return 2;

            Entity entity = NewEntity();
            entityDict[id] = entity;
            entity.id = id;
            entity.posX = gx;
            entity.posY = gy;
            entity.mask = mask;

            // 先取事件列表，这样就不会包含自己
            (int vx, int vy, int vdx, int vdy) = GetVisualRange(gx, gy);
            EntityEnterRange(entity, vx, vy, vdx, vdy, list);

            InsertGridEntity(gx, gy, entity);   // 插入到格子内

            return 0;
        }

        // 处理实体退出场景
        public int ExitEntity(int id, List<Entity> list = null)
        {
            bool hasEntity = entityDict.TryGetValue(id, out Entity entity);
            if (hasEntity == false) return 1;

            entityDict.Remove(id);

            bool ok = RemoveGridEntity(entity.posX, entity.posY, entity);

            // 是否需要返回关注自己离开场景的实体列表
            List<Entity> interestMe = entity.interestMe;
            if (list != null)
            {
                list.AddRange(interestMe);
            }

            // 从别人的 interestMe 列表中删除（自己关注event才有可能出现在别人的interest列表中）
            if ((entity.mask & INTEREST) == 1)
            {
                (int x, int y, int dx, int dy) = GetVisualRange(entity.posX, entity.posY);

                // 把自己的列表清空，这样从自己列表中删除时就不用循环了
                interestMe.Clear();
                EntityExitRange(entity, x, y, dx, dy);
            }

            DelEntity(entity);

            return ok ? 0 : -1;
        }

        // 处理实体进入某个范围
        private void EntityEnterRange(Entity entity, int x, int y, int dx, int dy, List<Entity> list = null)
        {
            RawEachRangeEntity(x, y, dx, dy, other =>
            {
                // 自己对其他实体感兴趣，就把自己加到对方列表，这样对方有变化时才会推送数据给自己
                if ((entity.mask & INTEREST) == 1)
                {
                    other.interestMe.Add(entity);
                }

                // 别人对自己感兴趣，把别人加到自己的列表，这样自己有变化才会发数据给对方
                if ((other.mask & INTEREST) == 1)
                {
                    entity.interestMe.Add(other);
                }

                // 无论是否interest，都返回需要触发aoi事件的实体。假如玩家进入场景时，怪物对他不
                // interest，但需要把怪物的信息发送给玩家，这步由上层筛选
                if (list != null)
                {
                    list.Add(other);
                }
            });
        }

        // 处理实体退出某个范围
        private void EntityExitRange(Entity entity, int x, int y, int dx, int dy, List<Entity> list = null)
        {
            RawEachRangeEntity(x, y, dx, dy, other =>
            {
                if ((entity.mask & INTEREST) == 1)
                {
                    RemoveEntityFromList(other.interestMe, entity);
                }

                if ((other.mask & INTEREST) == 1)
                {
                    RemoveEntityFromList(entity.interestMe, other);
                } 

                if (list != null)
                {
                    list.Add(other);
                }
            });
        }

        /// <summary>
        /// 更新实体位置
        /// </summary>
        /// <param name="x">物体像素坐标X</param>
        /// <param name="y">物体像素坐标Y</param>
        /// <param name="listIn">接收实体进入的实体列表</param>
        /// <param name="listOut">接收实体消失的实体列表</param>
        /// <returns> <0错误，0正常，>0正常，但做了特殊处理 </returns>
        public int UpdateEntity(int id, int x, int y, List<Entity> listIn = null, List<Entity> listOut = null)
        {
            // 检测坐标
            int gx = x / pixGrid;
            int gy = y / pixGrid;
            if (!ValidPos(gx, gy, gx, gy)) return -1;

            Entity entity = GetEntity(id);

            if(entity == null)
            {
                Console.WriteLine($"{id} 不存在");
                return -2;
            }

            // 在同一个格子内移动AOI这边其实不用做任何处理
            if( gx == entity.posX && gy == entity.posY)
            {
                return 0;
            }

            // 获取旧视野
            (int oldX, int oldY, int oldDX, int oldDY) = GetVisualRange(entity.posX, entity.posY);

            // 获取新视野
            (int newX, int newY, int newDX, int newDY) = GetVisualRange(gx, gy);

            // 求矩形交集 intersection
            // 1.分别取两个矩形左上角坐标中x、y最大值作为交集矩形的左上角的坐标
            // 2.分别取两个矩形的右下角坐标x、y最小值作为交集矩形的右下角坐标
            // 3.判断交集矩形的左上角坐标是否在右下角坐标的左上方。如果否则没有交集
            bool intersection = true;
            int itX = Math.Max(oldX, newX);
            int itY = Math.Max(oldY, newY);
            int itDX = Math.Min(oldDX, newDX);
            int itDY = Math.Min(oldDY, newDY);
            if (itX > itDX || itY > itDY)
                intersection = false;

            // 从旧格子退出
            bool ok = RemoveGridEntity(entity.posX, entity.posY, entity);

            // 根据新旧格子是否有交集分为下面两种情况：
            // 1.没有交集：这种情况处理简单，直接把原格子附近进行entity退出更新，新格子附近进行entity进入更新
            // 2.有交集：相交的格子不需要进行entity的退出或进入更新，只对不相交的区域进行退出更新（原格子附近） 或者 进入更新（新格子附近）。

            // 由于事件列表不包含自己，退出格子后先取列表再进入新格子
            
            // 1.没有交集
            // 交集区域内玩家，触发更新事件
            // 旧视野区域，触发退出
            // 新视野区域，触发进入
            if (!intersection)
            {
                // 把列表清空，退出时减少查找时间
                entity.interestMe.Clear();
                EntityExitRange(entity, oldX, oldY, oldDX, oldDY, listOut);
                EntityEnterRange(entity, newX, newY, newDX, newDY, listIn);

                // 进入新格子
                entity.posX = gx;
                entity.posY = gy;
                InsertGridEntity(gx, gy, entity);
                return ok ? 0 : -2;
            }

            // 2.有交集
            // 对原来的以(oldX,oldY)为中心的附近的格子，进行entity退出处理
            // 需要排除交集区域，因为交集的格子仍在新坐标的附近
            // 因为视野这个矩形不可以旋转，所以交集区域总在矩形的4个角上
            // 按x轴筛选（即一竖条一竖条的更新，比如更新(1,0)(1,3)，然后(2,0)(2,3)，遇到交集处理y坐标），
            // 那y轴就有几种情况：1无效，2取上半段，3取下段

            for (int ix = oldX; ix<=oldDX; ix++)
            {
                int iy = oldY;
                int idy = oldDY;
                // 当x轴进入交集区域时，需要处理y轴，交集，要么在上边，要么在下边
                if(ix >= itX && ix <= itDX)
                {
                    // 交集是原格子的 左上角 或者 右上角
                    if(oldDY > itDY)
                    {
                        iy = itDY + 1;
                        idy = oldDY;
                    }
                    // 交集是原格子的 左下角 或者 右下角
                    else if ( oldY < itY)
                    {
                        iy = oldY;
                        idy = itY - 1;
                    }
                    else
                    {
                        continue; // 无效
                    }
                }

                if (iy > idy)
                {
                    Console.WriteLine($"坐标非法：{iy} 大于 {idy}");
                    return -2;
                }

                EntityExitRange(entity, ix, iy, ix, idy, listOut);
            }

            // 对新的以(newX,newY)为中心的附近的格子，进行entity进入处理
            for (int ix = newX; ix <= newDX; ix++)
            {
                int iy = newY;
                int idy = newDY;
                if(ix >= itX && ix <= itDX)
                {
                    // 交集是原格子的 左上角 或者 右上角
                    if (newDY > itDY)
                    {
                        iy = itDY + 1;
                        idy = newDY;
                    }
                    // 交集是原格子的 左下角 或者 右下角
                    else if (newY < itY)
                    {
                        iy = newY;
                        idy = itY - 1;
                    }
                    else
                    {
                        continue; // 无效
                    }
                }
                
                if (iy > idy)
                {
                    Console.WriteLine($"坐标非法：{iy} 大于 {idy}");
                    return -2;
                }

                EntityEnterRange(entity, ix, iy, ix, idy, listIn);
            }

            // 进入新格子
            entity.posX = gx;
            entity.posY = gy;
            InsertGridEntity(gx, gy, entity);
            return ok ? 0 : -2;
        }

        #region 工具方法

        // 获取实体
        public Entity GetEntity(int id)
        {
            entityDict.TryGetValue(id, out Entity entity);
            return entity;
        }

        // 根据像素坐标判断是否在同一个格子内
        public bool IsSamePos(int x, int y, int dx, int dy)
        {
            bool res = x / pixGrid == dx / pixGrid && y / pixGrid == dy / pixGrid;
            return res;
        }

        // 检验格子坐标是否合法
        private bool ValidPos(int x, int y, int dx, int dy)
        {
            if(x < 0 || y < 0 || dx>= width || dy >= height)
            {
                Console.WriteLine($"非法的坐标 grid pos ({x},{y}) ({dx},{dy}), range ({width-1},{height-1})");
                return false;
            }
            return true;
        }

        // 获取(x,y)在entityGrid中的index
        private List<Entity> GetListInEntityGrid(int x, int y)
        {
            if (entityGrid == null || width == 0) return null;
            return entityGrid[x + y * width];
        }

        // 设置(x,y)在entityGrid中的列表
        private void SetListInEntityGrid(int x, int y, List<Entity> list)
        {
            entityGrid[x + y * width] = list;
        }

        public void PrintGrid()
        {
            int j = 0;
            for (int i = 0; i < entityGrid.Length; i++)
            {
                if (j == width)
                {
                    j = 0;
                    Console.WriteLine();
                }

                var list = entityGrid[i];
                if (list == null) Console.Write($"{0} ");
                else Console.Write($"{list.Count} ");
                j++;
            }
            Console.WriteLine();
        }

        #endregion

    }
}
