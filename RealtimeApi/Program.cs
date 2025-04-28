
using System.Text;
using RealtimeApi.Configurations;
using RealtimeApi.Services;

namespace RealtimeApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Configure console logging
            builder.Logging.AddConsole(configure =>
            {
                configure.FormatterName = "datetime";
            });

            builder.Services.Configure<TcpClientSetting>(builder.Configuration.GetSection("TcpClientSetting"));
            builder.Services.Configure<TcpServerSetting>(builder.Configuration.GetSection("TcpServerSetting"));

            // Add services to the container.
            builder.Services.AddSingleton<TcpServerFactory>();
            builder.Services.AddSingleton<TcpSocketServerService>();
            builder.Services.AddSingleton<ITcpServerManager>(provider => provider.GetRequiredService<TcpSocketServerService>());
            builder.Services.AddHostedService(provider => provider.GetRequiredService<TcpSocketServerService>());
            builder.Services.AddHostedService<TcpSocketClientService>();

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
