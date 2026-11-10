using System.Data;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Mapping;

public interface IProductMapper
{
    ProductRecord MapRow(IDataRecord record);
    IReadOnlyList<string> ValidateMapping();
}
