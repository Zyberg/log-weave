using Microsoft.Extensions.Logging;

namespace SmokeTest
{
   public class Test
   {
      private readonly ILogger<Test> _logger;

      public Test(ILogger<Test> logger) {
        _logger = logger;
      }

      public int DoSomething(int oo, int bb) {
        _logger.LogInformation("oo={0}, bb={1}", oo, bb );
        return oo + bb;
      }
   } 
}
