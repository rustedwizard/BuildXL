config({
    resolvers: [
        {
            kind: "DScript",
            modules: [
                { moduleName: "fragment3", projects: [f`./fragment3.dsc`] },
                f`${Environment.getPathValue("BUILDXL_BIN")}/Sdk/Sdk.Transformers/package.config.dsc`,
            ]
        },
    ],
    mounts: [
        {
            name: a`Out`,
            // The env variable is used here to ensure that all fragments use the same path.
            path: p`${Environment.getPathValue("OUTPUT_DIR")}`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true
        },
    ]
});