namespace Frame.Pooling
{
    /// <summary>
    /// 专门用来给objectpool对应的池对象来实现的接口
    /// </summary> <summary>
    /// 
    /// </summary>
    public interface IResettablePoolItem
    {
        void ResetForPool();
    }
}
