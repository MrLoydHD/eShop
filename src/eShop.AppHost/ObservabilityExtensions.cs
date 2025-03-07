using System;
using System.IO;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace eShop.AppHost;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Adds observability infrastructure (Jaeger and Grafana) to the application
    /// </summary>
    public static (IResourceBuilder<ContainerResource>, IResourceBuilder<ContainerResource>, IResourceBuilder<ContainerResource>) 
        AddObservabilityInfrastructure(this IDistributedApplicationBuilder builder)
    {
        // Get absolute paths to config files and directories
        var baseDirectory = AppContext.BaseDirectory;
        var solutionDir = Path.GetFullPath(Path.Combine(baseDirectory, "../../../../.."));
        
        var grafanaDashboardsPath = Path.GetFullPath(Path.Combine(solutionDir, "observability/grafana/dashboards"));
        var grafanaProvisioningPath = Path.GetFullPath(Path.Combine(solutionDir, "observability/grafana/provisioning"));
        var prometheusConfigPath = Path.GetFullPath(Path.Combine(solutionDir, "observability/prometheus/prometheus.yml"));
        var otelPath = Path.GetFullPath(Path.Combine(solutionDir, "observability/otel-collector/config.yaml"));
        var jaegerPath = Path.GetFullPath(Path.Combine(solutionDir, "observability/jaeger/config.yml"));
        
        Console.WriteLine($"Using grafana dashboards at: {grafanaDashboardsPath}");
        Console.WriteLine($"Using grafana provisioning at: {grafanaProvisioningPath}");
        Console.WriteLine($"Using prometheus config at: {prometheusConfigPath}");

        // Add Jaeger for distributed tracing visualization
        var jaeger = builder.AddContainer("jaeger", "jaegertracing/jaeger", "2.3.0")
            .WithEndpoint(port: 16686, targetPort: 16686, name: "jaeger-ui")
            .WithEndpoint(port: 4317, targetPort: 4317, name: "jaeger-otlp-grpc")
            .WithEndpoint(port: 4318, targetPort: 4318, name: "jaeger-otlp-http")
            .WithEndpoint(port: 14250, targetPort: 14250, name: "jaeger-collector")
            .WithEndpoint(port: 9411, targetPort: 9411, name: "jaeger-zipkin")
            .WithEndpoint(port: 6831, targetPort: 6831, name: "jaeger-thrift-udp")
            .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true")
            .WithEnvironment("COLLECTOR_ZIPKIN_HOST_PORT", "9411")
            .WithBindMount(jaegerPath, "/etc/jaeger/config.yml")
            .WithArgs("--config", "/etc/jaeger/config.yml");

        // Add Prometheus for metrics with the existing config file
        var prometheus = builder.AddContainer("prometheus", "prom/prometheus:latest")
            .WithEndpoint(port: 9090, targetPort: 9090, name: "prometheus-ui")
            .WithBindMount(prometheusConfigPath, "/etc/prometheus/prometheus.yml");

        // Add Grafana for dashboards
        var grafana = builder.AddContainer("grafana", "grafana/grafana:latest")
            .WithEndpoint(port: 3000, targetPort: 3000, name: "grafana-ui")
            .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", "admin")
            .WithEnvironment("GF_SECURITY_ADMIN_USER", "admin")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
            .WithEnvironment("GF_INSTALL_PLUGINS", "grafana-clock-panel")
            .WithEnvironment("GF_LOG_LEVEL", "debug") // Add debug logging
            .WithEnvironment("GF_DASHBOARDS_MIN_REFRESH_INTERVAL", "5s") // Allow faster refresh
            .WithBindMount(grafanaDashboardsPath, "/var/lib/grafana/dashboards")
            .WithBindMount(grafanaProvisioningPath, "/etc/grafana/provisioning");

        // Add otel-collector
        var otel = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib", "0.120.0")
            .WithBindMount(otelPath, "/etc/otel-collector.yaml")
            .WithArgs("--config", "/etc/otel-collector.yaml")
            .WithEndpoint(4319, 4317, name: "otlp-grpc")       // OTLP gRPC (changed host port)
            .WithEndpoint(4320, 4318, name: "otlp-http")       // OTLP HTTP (changed host port)
            .WithEndpoint(8890, 8889, name: "prometheus-port"); // Prometheus metrics (changed host port)

        // Set up dependencies
        prometheus.WaitFor(jaeger);
        grafana.WaitFor(prometheus);
        grafana.WaitFor(jaeger);

        return (prometheus, jaeger, grafana);
    }

    /// <summary>
    /// Configures a service to send telemetry to the observability infrastructure
    /// </summary>
    public static IResourceBuilder<T> WithObservability<T>(
        this IResourceBuilder<T> builder, 
        string serviceName, 
        IResourceBuilder<ContainerResource> jaeger) 
        where T : IResourceWithEnvironment
    {
        // Get the fixed IP address for Jaeger instead of relying on DNS resolution
        var jaegerEndpoint = "http://localhost:4319";
        
        return builder
            // Add direct connection to make debugging easier
            .WithEnvironment("ConnectionStrings__Jaeger", jaegerEndpoint)
            // Add standard OpenTelemetry configuration
            .WithEnvironment("OTEL_SERVICE_NAME", serviceName)
            .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", $"service.name={serviceName}")
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerEndpoint)
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WithEnvironment("OTEL_METRICS_EXPORTER", "otlp,prometheus")
            .WithEnvironment("OTEL_LOGS_EXPORTER", "otlp")
            .WithEnvironment("OTEL_TRACES_EXPORTER", "otlp")
            .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
            .WithEnvironment("OTEL_PROPAGATORS", "tracecontext,baggage")
            // Explicitly enable instrumentation
            .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "Microsoft.Extensions.Telemetry.Abstractions");
    }
}
