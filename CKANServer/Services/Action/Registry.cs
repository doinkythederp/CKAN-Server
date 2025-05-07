using System.Diagnostics;
using CKAN;
using CKAN.Extensions;
using CKAN.Versioning;
using CKANServer.Utils;
using Google.Protobuf.WellKnownTypes;

namespace CKANServer.Services.Action;

public partial class CkanAction
{
    private async Task WriteRegistryOpReply(RegistryOperationResult result, string? errorDetails = null)
    {
        var reply = new RegistryOperationReply
        {
            Result = result,
        };
        if (errorDetails != null) reply.ErrorDetails = errorDetails;

        await WriteMessageAsync(new ActionReply
        {
            RegistryOperationReply = reply,
        });
    }

    private async Task WriteError(TooManyModsProvideKraken ex)
    {
        var reply = new RegistryOperationReply
        {
            Result = RegistryOperationResult.RorTooManyModsProvide,
            TooManyModsProvideError = new TooManyModsProvideError
            {
                RequestedVirtualModule = ex.requested,
                RequestingModule = ex.requester.ToRef(),
                Candidates = { ex.modules.Select(m => m.ToRef()) },
            },
        };
        if (ex.choice_help_text != null) reply.ErrorDetails = ex.choice_help_text;

        await WriteMessageAsync(new ActionReply
        {
            RegistryOperationReply = reply,
        });
    }

    public async Task PrepopulateRegistry(RegistryPrepopulateRequest request, bool isRetry = false)
    {
        logger.LogInformation("Prepopulating registry for instance {Name}", request.InstanceName);
        var instance = await InstanceFromName(request.InstanceName);
        if (instance == null) return;

        var regMgr = RegistryManagerFor(instance);

        try
        {
            RepoManager.Prepopulate(regMgr.registry.Repositories.Values.ToList(), Progress);
        }
        catch (RegistryInUseKraken ex)
        {
            if (!isRetry && request.ForceLock)
            {
                File.Delete(ex.lockfilePath);
                await PrepopulateRegistry(request, true);
                return;
            }

            await WriteRegistryOpReply(RegistryOperationResult.RorRegistryInUse);
        }
        catch (NotKSPDirKraken ex)
        {
            await WriteInstanceOpReply(InstanceOperationResult.IorNotAnInstance, ex.path);
            return;
        }

        await WriteRegistryOpReply(RegistryOperationResult.RorSuccess);
    }

    public async Task AvailableModules(RegistryAvailableModulesRequest request)
    {
        logger.LogInformation("Fetching modules available to instance {Name}", request.InstanceName);
        var instance = await InstanceFromName(request.InstanceName);
        if (instance == null) return;

        var regMgr = RegistryManagerFor(instance);
        var modules = RepoManager.GetAllAvailableModules(regMgr.registry.Repositories.Values).ToList();

        var remaining = (uint)modules.Count;

        foreach (var chunk in modules.Chunk(500))
        {
            remaining -= (uint)chunk.Length;

            await WriteMessageAsync(new ActionReply
            {
                RegistryOperationReply = new RegistryOperationReply
                {
                    Result = RegistryOperationResult.RorSuccess,
                    AvailableModules = new RegistryAvailableModulesReply
                    {
                        Modules =
                        {
                            chunk
                                .Select(m => m.ToProto(RepoManager, regMgr.registry))
                                .OfType<Module>(),
                        },
                        Remaining = remaining,
                    },
                },
            });
        }
    }

    /// <summary>
    /// Retrieve information about the modules available to the given instance, such as whether they are installed
    /// and if they can be upgraded.
    /// </summary>
    public async Task ModuleStates(RegistryModuleStatesRequest request)
    {
        logger.LogInformation("Fetching module states for instance {Name}", request.InstanceName);
        var instance = await InstanceFromName(request.InstanceName);
        if (instance == null) return;

        if (request.CompatOptions != null)
        {
            ApplyCompatOptionsTo(instance, request.CompatOptions);
        }

        var registry = RegistryManagerFor(instance).registry;

        var states = new Dictionary<string, ModuleState>();

        // First search for all installed modules. Anything returned by CheckUpgradeable is already installed.
        // Auto-detected mods aren't handled yet.

        var upgradableLists = registry.CheckUpgradeable(instance,
            [..request.HeldModuleIdents]);

        foreach (var (isUpgradable, modules) in upgradableLists)
        {
            foreach (var module in modules)
            {
                var state = new ModuleState
                {
                    Identifier = module.identifier,
                    CanBeUpgraded = isUpgradable,
                    IsCompatible = true,
                    CurrentRelease = module.version.ToString(),
                };
                QueryInstallState(module.identifier, registry, state);
                states[module.identifier] = state;
            }
        }

        // Now add everything else (auto-detected mods and uninstalled mods).
        // We filter out mods that are both installed & managed to prevent duplicates.

        var sorter =
            registry.SetCompatibleVersion(instance.StabilityToleranceConfig, instance.VersionCriteria());

        var items = sorter.LatestCompatible
            .Select(m => (true, m))
            .Concat(sorter.LatestIncompatible
                .Select(m => (false, m)));

        foreach (var (compatible, module) in items)
        {
            if (states.ContainsKey(module.identifier)) continue;

            var state = new ModuleState
            {
                Identifier = module.identifier,
                IsCompatible = compatible,
                CurrentRelease = module.version.ToString(),
            };
            QueryInstallState(module.identifier, registry, state);
            states[module.identifier] = state;
        }

        await WriteMessageAsync(new ActionReply
        {
            RegistryOperationReply = new RegistryOperationReply
            {
                Result = RegistryOperationResult.RorSuccess,
                ModuleStates = new RegistryModuleStatesReply
                {
                    States = { states.Values },
                },
            },
        });
    }

    private static void ApplyCompatOptionsTo(GameInstance instance, Instance.Types.CompatOptions options)
    {
        var stabilityTolerance = instance.StabilityToleranceConfig;

        stabilityTolerance.OverallStabilityTolerance =
            options.StabilityTolerance.FromProto() ?? ReleaseStatus.stable;

        var overrides = options.StabilityToleranceOverrides;
        var overriddenIds = stabilityTolerance.OverriddenModIdentifiers
            .Union(overrides.Keys)
            .ToArray();

        foreach (var id in overriddenIds)
        {
            ReleaseStatus? tolerance = null;
            if (overrides.ContainsKey(id))
            {
                tolerance = options.StabilityToleranceOverrides[id].FromProto();
            }

            stabilityTolerance.SetModStabilityTolerance(id, tolerance);
        }

        stabilityTolerance.Save();

        if (options.VersionCompatibility is { } versionCompat)
        {
            instance.SetCompatibleVersions(versionCompat
                .CompatibleVersions
                .Select(version => version.ToCkan())
                .ToList());
        }
    }

    /// <summary>
    /// Fills the <c>install</c> field in <c>state</c> with the installation status of the module with the identifier
    /// <c>moduleId</c> using information from the given <c>querier</c>.
    /// </summary>
    private static void QueryInstallState(string moduleId, IRegistryQuerier querier, ModuleState state)
    {
        var installVersion = querier.InstalledVersion(moduleId, with_provides: false);

        if (installVersion is UnmanagedModuleVersion unmanagedVersion)
        {
            state.UnmanagedInstall = new UnmanagedInstalledModule();
            if (!unmanagedVersion.IsUnknownVersion)
            {
                state.UnmanagedInstall.ReleaseVersion = unmanagedVersion.ToString();
            }

            return;
        }

        var installedModule = querier.InstalledModule(moduleId);
        if (installedModule is null) return;

        state.ManagedInstall = new ManagedInstalledModule
        {
            ReleaseVersion = installedModule.Module.version.ToString(),
            InstallDate = Timestamp.FromDateTime(installedModule.InstallTime),
            IsAutoInstalled = installedModule.AutoInstalled,
        };
    }

    /// <summary>
    /// Queries the list of releases of the given module that are compatible with the given instance. 
    /// </summary>
    public async Task CompatibleModuleReleases(RegistryCompatibleModuleReleasesRequest request)
    {
        logger.LogInformation(
            "Fetching versions of module {Module} compatible with instance {Name}",
            request.ModuleId,
            request.InstanceName);

        var instance = await InstanceFromName(request.InstanceName);
        if (instance == null) return;

        if (request.CompatOptions != null)
        {
            ApplyCompatOptionsTo(instance, request.CompatOptions);
        }

        var registry = RegistryManagerFor(instance).registry;

        IEnumerable<CkanModule> releases;
        try
        {
            releases = registry.AvailableByIdentifier(request.ModuleId);
        }
        catch (ModuleNotFoundKraken ex)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorModuleNotFound, ex.module);
            return;
        }

        var compatibleReleases = releases.Where(module =>
            {
                var stabilityTolerance = instance.StabilityToleranceConfig.ModStabilityTolerance(module.identifier)
                                         ?? instance.StabilityToleranceConfig.OverallStabilityTolerance;

                return module.IsCompatible(instance.VersionCriteria())
                       && module.release_status <= stabilityTolerance
                       && ModuleInstaller.CanInstall([module],
                           RelationshipResolverOptions.DependsOnlyOpts(instance.StabilityToleranceConfig),
                           registry, instance.game, instance.VersionCriteria());
            })
            .Select(module => module.version.ToString());

        await WriteMessageAsync(new ActionReply
        {
            RegistryOperationReply = new RegistryOperationReply
            {
                Result = RegistryOperationResult.RorSuccess,
                CompatibleModuleReleases = new RegistryCompatibleModuleReleasesReply
                {
                    CompatibleVersions = { compatibleReleases },
                },
            },
        });
    }

    public async Task OptionalDependencies(RegistryOptionalDependenciesRequest request)
    {
        logger.LogInformation(
            "Loading optional dependencies for {Count} modules",
            request.Modules.Count);

        var instance = await InstanceFromName(request.InstanceName);
        if (instance == null) return;

        var registry = RegistryManagerFor(instance).registry;

        ICollection<CkanModule> modules;
        try
        {
            modules = request.Modules
                .Select(reference => registry.GetModuleByVersion(reference.Id, reference.Version)!)
                .ToArray();
        }
        catch (NullReferenceException)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorModuleNotFound);
            return;
        }

        try
        {
            ModuleInstaller.FindRecommendations(instance, modules, new List<CkanModule>(modules), registry,
                out var recommendations,
                out var suggestions,
                out var supporters);

            var reply = new RegistryOptionalDependenciesReply
            {
                Recommended = { recommendations.Select(pair => MakeDependency(pair.Key, pair.Value.Item2)) },
                Suggested = { suggestions.Select(pair => MakeDependency(pair.Key, pair.Value)) },
                Supporters = { supporters.Select(pair => MakeDependency(pair.Key, pair.Value)) },
                InstallableRecommended =
                    { recommendations.Where(pair => pair.Value.Item1).Select(pair => pair.Key.identifier) },
            };

            await WriteMessageAsync(new ActionReply
            {
                RegistryOperationReply = new RegistryOperationReply
                {
                    Result = RegistryOperationResult.RorSuccess,
                    OptionalDependencies = reply,
                },
            });
        }
        catch (TooManyModsProvideKraken ex)
        {
            await WriteError(ex);
        }

        return;

        static RegistryOptionalDependenciesReply.Types.Dependency MakeDependency(
            CkanModule module,
            IEnumerable<string> sources)
        {
            return new RegistryOptionalDependenciesReply.Types.Dependency
            {
                Module = module.ToRef(),
                Sources = { sources },
            };
        }
    }

    public async Task PerformInstall(RegistryPerformInstallRequest request)
    {
        logger.LogInformation("Performing installation");

        var instance = await InstanceFromName(request.InstanceName);
        if (instance == null) return;

        var registryManager = RegistryManagerFor(instance);
        var registry = registryManager.registry;

        if (InstanceManager.Cache == null)
        {
            throw new NullReferenceException("Cannot perform an install when the InstanceManager cache is not set");
        }

        var pendingInstallOrUpgrade = request.ModsToInstall
            .Select(reference =>
            {
                var module = registry.GetModuleByVersion(reference.Id, reference.Version);
                if (module == null)
                {
                    throw new ModuleNotFoundKraken(reference.Id, reference.Version);
                }

                return module;
            })
            .ToList();

        var pendingInstall = new List<CkanModule>();
        var pendingUpgrade = new List<CkanModule>();

        foreach (var ckanModule in pendingInstallOrUpgrade)
        {
            if (registry.IsInstalled(ckanModule.identifier, with_provides: false))
            {
                pendingInstall.Add(ckanModule);
            }
            else
            {
                pendingUpgrade.Add(ckanModule);
            }
        }

        var versionCriteria = instance.VersionCriteria();
        var pendingReplacements = request.ModsToReplace
            .Select(identifier =>
                registry.GetReplacement(identifier, instance.StabilityToleranceConfig, versionCriteria))
            .OfType<ModuleReplacement>()
            .ToList();

        HashSet<string>? possibleConfigOnlyDirs = null;
        var installer = new ModuleInstaller(instance, InstanceManager.Cache, User);
        var resolverOpts = new RelationshipResolverOptions(instance.StabilityToleranceConfig)
        {
            with_all_suggests = false,
            with_suggests = false,
            with_recommends = false,
            without_toomanyprovides_kraken = false,
            without_enforce_consistency = false,
        };
        var downloader =
            new NetAsyncModulesDownloader(User, InstanceManager.Cache, cancelToken: Context.CancellationToken);


        try
        {
            using var transaction = CkanTransaction.CreateTransactionScope();

            if (request.ModsToRemove.Count > 0)
            {
                installer.UninstallList(request.ModsToRemove, ref possibleConfigOnlyDirs, registryManager,
                    ConfirmPrompt: false,
                    installing: pendingInstall);
            }

            if (pendingInstall.Count > 0)
            {
                installer.InstallList(pendingInstall, resolverOpts, registryManager, ref possibleConfigOnlyDirs,
                    ConfirmPrompt: false,
                    downloader: downloader);
            }

            if (pendingUpgrade.Count > 0)
            {
                installer.Upgrade(pendingUpgrade, downloader, ref possibleConfigOnlyDirs, registryManager,
                    ConfirmPrompt: false);
            }

            if (request.ModsToReplace.Count > 0)
            {
                installer.Replace(pendingReplacements, resolverOpts, downloader, ref possibleConfigOnlyDirs,
                    registryManager);
            }

            if (possibleConfigOnlyDirs != null)
            {
                logger.LogWarning("Possible config only dirs: {Dirs}", possibleConfigOnlyDirs);
                // TODO: do something about these
            }

            transaction.Complete();

            await WriteMessageAsync(new ActionReply
            {
                RegistryOperationReply = new RegistryOperationReply
                {
                    Result = RegistryOperationResult.RorSuccess,
                    PerformInstall = new RegistryPerformInstallResponse(),
                },
            });
        }
        catch (CancelledActionKraken)
        {
            return;
        }
        catch (FileExistsKraken ex)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorFileExists, ex.filename);
        }
        catch (DownloadErrorsKraken ex)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorDownloadFailed, ex.ToString());
        }
        catch (ModuleDownloadErrorsKraken ex)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorDownloadFailed, ex.ToString());
        }
        catch (RequestThrottledKraken ex)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorNeedsAuthtoken, ex.Message);
        }
        catch (InconsistentKraken ex)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorInconsistent, ex.Message);
        }
        catch (TooManyModsProvideKraken ex)
        {
            // TODO: implement custom handler
            throw;
        }
        catch (BadMetadataKraken ex)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorBadMetadata, ex.Message);
        }
        catch (ModuleNotFoundKraken ex)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorModuleNotFound, ex.Message);
        }
        catch (ModNotInstalledKraken ex)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorNotInstalled, ex.mod);
        }
        catch (DllLocationMismatchKraken ex)
        {
            await WriteRegistryOpReply(RegistryOperationResult.RorDllLocationMismatch, ex.Message);
        }
    }
}