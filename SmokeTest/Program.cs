// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

var b = new Name();

b.SomeMethodPublic();


return;

public class Name
{
  public Name()
  {
    var a = 1;
  } 

  public void SomeMethodPublic() {
    Console.WriteLine("Hello, some public method!");
  }
}
