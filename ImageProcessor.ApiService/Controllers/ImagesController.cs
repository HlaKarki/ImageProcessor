using System.Security.Claims;
using ImageProcessor.ApiService.Models.DTOs;
using ImageProcessor.ApiService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImageProcessor.ApiService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ImagesController(S3Service s3, JobService jobService) : ControllerBase
{
    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromForm] ImageUploadRequest request)
    {
        
        // validate the file extension and mime type
        var extension = Path.GetExtension(request.file.FileName).ToLowerInvariant();

        if (!ImageUploadConstants.AllowedExtensions.Contains(extension) || !ImageUploadConstants.AllowedMimeTypes.Contains(request.file.ContentType))
        {
            return BadRequest("Only JPEG, PNG, and WEBP images are allowed.");
        }
        
        // validate file size
        if (request.file.Length > ImageUploadConstants.MaxFileSize)
        {
            return BadRequest("File size exceeds maximum allowed size (50MB).");
        }
        
        // extract user id from jwt claims
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var jobId = Guid.NewGuid().ToString();
        var url = await s3.UploadAsync(request.file, userId, jobId);

        var job = await jobService.CreateAsync(jobId, userId, url, request.file);
        
        return Ok(new { job.Id, job.OriginalUrl, job.Status});
    }

    [HttpGet("{jobId}")]
    public async Task<IActionResult> Get([FromRoute] Guid jobId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();
        var result = await jobService.GetByIdAsync(jobId, Guid.Parse(userId));
        return result is null ? NotFound($"No jobs exist with job id {jobId}") : Ok(result);
    }

    [HttpGet("")]
    public async Task<IActionResult> Get()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return NotFound();
        
        var result = await jobService.GetAllByUserAsync(Guid.Parse(userId));
        return Ok(result);
    }
}