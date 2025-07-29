using Microsoft.Extensions.Logging;

namespace SmokeTest
{
   public class Test
   {
      private readonly ILogger<Test> _logger;

      public Test(ILogger<Test> logger) {
        _logger = logger;
      }

      public void DoSomething(int a) {
        _logger.LogInformation("TEST log from inside DoSomething({a})", a);
      }
   } 
}
