using System;

namespace nostify;

public static class ProjectionInitializerFactory
{
    public static ProjectionInitializer BuildProjectionInitializer()
    {
        return new ProjectionInitializer();
    }
}