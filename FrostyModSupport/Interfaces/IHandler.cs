using Frosty.Sdk.Utils;

namespace Frosty.ModSupport.Interfaces;

public interface IHandler
{
    public void Load(Block<byte> inData);
}