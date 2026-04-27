using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OutboxNet.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OutboxNet.SampleApp.Data
{
    public class SampleEntitiesController : Controller
    {
        private readonly SampleAppContext _context;

        private readonly IOutboxPublisher outboxPublisher;

        public SampleEntitiesController(SampleAppContext context, IOutboxPublisher outboxPublisher)
        {
            _context = context;
            this.outboxPublisher = outboxPublisher;
        }

        // GET: SampleEntities
        public async Task<IActionResult> Index()
        {
            return View(await _context.SampleEntity.ToListAsync());
        }

        // GET: SampleEntities/Details/5
        public async Task<IActionResult> Details(Guid? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var sampleEntity = await _context.SampleEntity
                .FirstOrDefaultAsync(m => m.Id == id);
            if (sampleEntity == null)
            {
                return NotFound();
            }

            return View(sampleEntity);
        }

        // GET: SampleEntities/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: SampleEntities/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Data1")] SampleEntity sampleEntity)
        {
            if (ModelState.IsValid)
            {

                foreach (var item in Enumerable.Range(0, 100))
                {

                    var transaction = await _context.Database.BeginTransactionAsync();

                    sampleEntity.Id = Guid.NewGuid();
                    _context.Add(sampleEntity);
                    await _context.SaveChangesAsync();

                    await outboxPublisher.PublishAsync("eventType", new
                    {
                        id = sampleEntity.Id,
                        prop = $"{sampleEntity.Data1}_{item}"
                    });

                    await transaction.CommitAsync();
                }


                return RedirectToAction(nameof(Index));
            }
            return View(sampleEntity);
        }

        // GET: SampleEntities/Edit/5
        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var sampleEntity = await _context.SampleEntity.FindAsync(id);
            if (sampleEntity == null)
            {
                return NotFound();
            }
            return View(sampleEntity);
        }

        // POST: SampleEntities/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, [Bind("Id,Data1")] SampleEntity sampleEntity)
        {
            if (id != sampleEntity.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(sampleEntity);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SampleEntityExists(sampleEntity.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(sampleEntity);
        }

        // GET: SampleEntities/Delete/5
        public async Task<IActionResult> Delete(Guid? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var sampleEntity = await _context.SampleEntity
                .FirstOrDefaultAsync(m => m.Id == id);
            if (sampleEntity == null)
            {
                return NotFound();
            }

            return View(sampleEntity);
        }

        // POST: SampleEntities/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var sampleEntity = await _context.SampleEntity.FindAsync(id);
            if (sampleEntity != null)
            {
                _context.SampleEntity.Remove(sampleEntity);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool SampleEntityExists(Guid id)
        {
            return _context.SampleEntity.Any(e => e.Id == id);
        }
    }
}
