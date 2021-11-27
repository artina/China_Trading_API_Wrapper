using System;
using System.Net.NetworkInformation;
using ExcelDna.Integration;

namespace Common
{
  public class MyAddIn : IExcelAddIn
  {
    public void AutoOpen()
    {
    }

    public void AutoClose()
    {
    }
  }

  public static class Utils
  {
    public static bool IsInternetAvailable()
    {
      using (var p = new Ping())
      {
        try
        {
          return p.Send("8.8.8.8").Status == IPStatus.Success;
        }
        catch (Exception)
        {
          return false;
        }
      }
    }
  }
}
