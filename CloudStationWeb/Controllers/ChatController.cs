using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CloudStationWeb.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly string _sqlConn;

        public ChatController(IConfiguration config)
        {
            _sqlConn = config.GetConnectionString("SqlServer")!;
        }

        // GET: /Chat
        public IActionResult Index()
        {
            return View();
        }

        // GET: /Chat/History?room=general&limit=50
        [HttpGet]
        public async Task<IActionResult> History(string room = "general", int limit = 50, DateTime? since = null)
        {
            using var db = new SqlConnection(_sqlConn);
            string sql;
            object parameters;

            if (since.HasValue)
            {
                sql = @"SELECT TOP (@Limit) Id, ChatId, Room, UserId, UserName, FullName, Message, Timestamp
                        FROM ChatMessages
                        WHERE Room = @Room AND Timestamp >= @Since
                        ORDER BY Timestamp ASC";
                parameters = new { Room = room, Limit = limit, Since = since.Value };
            }
            else
            {
                sql = @"SELECT * FROM (
                            SELECT TOP (@Limit) Id, ChatId, Room, UserId, UserName, FullName, Message, Timestamp
                            FROM ChatMessages
                            WHERE Room = @Room
                            ORDER BY Timestamp DESC
                        ) sub ORDER BY Timestamp ASC";
                parameters = new { Room = room, Limit = limit };
            }

            var messages = await db.QueryAsync<ChatMessage>(sql, parameters);
            return Json(messages);
        }

        // GET: /Chat/Rooms
        [HttpGet]
        public async Task<IActionResult> Rooms()
        {
            using var db = new SqlConnection(_sqlConn);
            var rooms = await db.QueryAsync<dynamic>(@"
                SELECT Room, COUNT(*) AS MessageCount, MAX(Timestamp) AS LastActivity
                FROM ChatMessages
                GROUP BY Room
                ORDER BY LastActivity DESC");
            return Json(rooms);
        }
    }
}
