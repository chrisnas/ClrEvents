using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CountersWebApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RequestController : ControllerBase
    {
        // GET: api/Request
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET: api/Request/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            if (id == -1)
            {
                return $"pid = {Process.GetCurrentProcess().Id}";
            }
            else
            if ((id >= 0) && (id <= 2))
            {
                GC.Collect(id);
                return $"triggered GC {id}";
            }
            else
            if (id <= 10)
            {
                // trigger a given number of GCs up to 10
                TriggerGCs(id);
                return $"triggered {id} garbage collections";
            }

            return $"value = {id}";
        }

        private void TriggerGCs(int count)
        {
            for (int current = 0; current < count; current++)
            {
                GC.Collect(0);
            }
        }


        // POST: api/Request
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT: api/Request/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE: api/ApiWithActions/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
