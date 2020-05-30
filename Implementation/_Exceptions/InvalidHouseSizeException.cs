using System;
using System.Runtime.Serialization;

namespace Terraria.Plugins.CoderCow.HouseRegions {
  [Serializable]
  public class InvalidHouseSizeException: Exception {
    #region [Property: RestrictingConfig]
    private readonly Configuration.HouseSizeConfig restrictingConfig;

    public Configuration.HouseSizeConfig RestrictingConfig {
      get { return this.restrictingConfig; }
    }
    #endregion

    public InvalidHouseSizeException(string message, Exception inner = null): base(message, inner) {}

    public InvalidHouseSizeException(Configuration.HouseSizeConfig restrictingConfig): base("O tamanho de sua casa não está de acordo com o tamanho máximo para seu grupo.") {
      this.restrictingConfig = restrictingConfig;
    }

    public InvalidHouseSizeException(): base("O tamanho de sua casa não está de acordo com o tamanho máximo para seu grupo.") {}

    protected InvalidHouseSizeException(SerializationInfo info, StreamingContext context): base(info, context) {}
  }
}