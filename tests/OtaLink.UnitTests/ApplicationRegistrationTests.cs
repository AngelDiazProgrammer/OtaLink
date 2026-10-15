using Microsoft.Extensions.DependencyInjection;
using OtaLink.Application;

namespace OtaLink.UnitTests;

public class ApplicationRegistrationTests
{
    [Fact]
    public void AddApplication_devuelve_la_misma_coleccion_para_encadenar()
    {
        var services = new ServiceCollection();

        var result = services.AddApplication();

        Assert.Same(services, result);
    }
}
