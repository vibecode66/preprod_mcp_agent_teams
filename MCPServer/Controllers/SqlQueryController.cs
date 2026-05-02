using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace MCPServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SqlQueryController : ControllerBase
    {
        /// <summary>
        /// Runs a read-only SQL SELECT against your configured database.
        /// </summary>
        [HttpPost("execute")]
        public async Task<IActionResult> ExecuteQuery([FromBody] SqlQueryRequest request)
        {
            var result = await SqlTools.ExecuteReadonlySql(request.SqlQuery);
            return Content(result, "application/json");
        }

        /// <summary>
        /// Returns all tables in the database — used by the Function App agent for schema context.
        /// GET /api/sqlquery/tables
        /// </summary>
        [HttpGet("tables")]
        public async Task<IActionResult> GetTables()
        {
            var result = await SqlTools.ListTables();
            return Content(result, "application/json");
        }

        /// <summary>
        /// Health check for the MCP Server — used by the Function App agent.
        /// GET /api/sqlquery/health
        /// </summary>
        [HttpGet("health")]
        public IActionResult GetHealth()
        {
            return Ok(new { status = "healthy", service = "MCP Server", timestamp = DateTime.UtcNow });
        }
    }

    public class SqlQueryRequest
    {
        public string SqlQuery { get; set; }
    }
}