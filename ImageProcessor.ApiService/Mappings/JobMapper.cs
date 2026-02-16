using ImageProcessor.ApiService.Models.DTOs;
using ImageProcessor.Data.Models.Domain;
using Riok.Mapperly.Abstractions;

namespace ImageProcessor.ApiService.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)] // remove warnings for unmapped source members
public partial class JobMapper
{
    public partial JobResponse ToResponse(Job job);

    private static string MapStatus(JobStatus status) => status.ToString();
}