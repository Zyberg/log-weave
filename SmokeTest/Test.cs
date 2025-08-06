using Microsoft.Extensions.Logging;

namespace SmokeTest
{
   public class Test
   {
      private readonly ILogger<Test> _logger;

      public Test(ILogger<Test> logger) {
        _logger = logger;
      }

      private int SumNumbers(int a, int b) {
        return a + b;
      }

      private int ProductOfNumbers(int a, int b) {
        return a * b;
      }

      public int DoSomething(int oo, int bb) {
        _logger.LogInformation("oo={0}", oo);
        return SumNumbers(ProductOfNumbers(oo, 3), bb);
      }
   } 
}
