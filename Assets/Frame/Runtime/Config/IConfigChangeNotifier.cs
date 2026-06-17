using System;

namespace Frame.Config
{
    public interface IConfigChangeNotifier
    {
        event Action Changed;
    }
}
