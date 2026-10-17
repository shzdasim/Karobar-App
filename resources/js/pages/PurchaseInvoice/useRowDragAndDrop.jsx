import { useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";

const sameTarget = (a, b) => a === b || (a && b && a.kind === b.kind && a.index === b.index && a.after === b.after && a.groupPos === b.groupPos);
const DRAG_THRESHOLD = 4; // px of movement before a press becomes a drag (keeps clicks as clicks)
const EDGE = 48; // px from the scroller edge where auto-scroll kicks in

/**
 * Pointer-based drag & drop for purchase invoice rows and whole brand groups.
 * Uses Pointer Events (mouse, pen and touch) with a dedicated handle, a small
 * movement threshold, document-level listeners (rows re-render mid-drag),
 * auto-scroll near the edges of the items scroller and arrow-key fallback.
 *
 * @param rows        arrangedRows ({ item, index, brand_key, brand_name }) in display order
 * @param scrollerRef ref to the scrollable items container
 * @param onCommit    called with product row indices in their new order
 */
export default function useRowDragAndDrop(rows, scrollerRef, onCommit) {
  const [drag, setDrag] = useState(null); // { type: 'row', index } | { type: 'group', groupPos }, plus label/x/y
  const [dropTarget, setDropTarget] = useState(null); // { kind: 'row', index, after } | { kind: 'group', groupPos }
  const ghostRef = useRef(null);

  // Contiguous runs of the same brand, matching the brand headings shown in the table.
  const { groups, groupStart, groupOf } = useMemo(() => {
    const list = [];
    rows.forEach(row => {
      if (!row.item.product_id) return;
      const last = list[list.length - 1];
      if (last && String(last.brand_key) === String(row.brand_key) && last.brand_name === row.brand_name) last.indices.push(row.index);
      else list.push({ brand_key: row.brand_key, brand_name: row.brand_name, indices: [row.index] });
    });
    const start = new Map(list.map((group, pos) => [group.indices[0], pos]));
    const of = new Map(list.flatMap((group, pos) => group.indices.map(index => [index, pos])));
    return { groups: list, groupStart: start, groupOf: of };
  }, [rows]);

  const productOrder = () => groups.flatMap(group => group.indices);

  const moveRowTo = (from, target, after) => {
    if (from === target) return;
    const order = productOrder().filter(index => index !== from);
    const pos = order.indexOf(target);
    if (pos < 0) return;
    order.splice(after ? pos + 1 : pos, 0, from);
    onCommit(order);
  };

  // Dropping a brand on a group above places it before that group; below places it after.
  const moveGroupTo = (from, to) => {
    if (from === to || to < 0 || to >= groups.length) return;
    const list = [...groups];
    const [moved] = list.splice(from, 1);
    list.splice(to, 0, moved);
    onCommit(list.flatMap(group => group.indices));
  };

  const resolveDrop = (x, y, payload) => {
    const el = document.elementFromPoint(x, y)?.closest("[data-dnd-row], [data-dnd-group]");
    if (!el) return null;
    const groupAttr = el.getAttribute("data-dnd-group");
    if (payload.type === "row") {
      if (groupAttr !== null) {
        const group = groups[Number(groupAttr)];
        return group ? { kind: "row", index: group.indices[0], after: false } : null;
      }
      const rect = el.getBoundingClientRect();
      return { kind: "row", index: Number(el.getAttribute("data-dnd-row")), after: y > rect.top + rect.height / 2 };
    }
    const groupPos = groupAttr !== null ? Number(groupAttr) : groupOf.get(Number(el.getAttribute("data-dnd-row")));
    return groupPos === undefined || groupPos === payload.groupPos ? null : { kind: "group", groupPos };
  };

  const startDrag = (event, payload) => {
    if (event.button !== 0) return; // primary button / touch / pen only
    const { pointerId, clientX: startX, clientY: startY } = event;
    let active = false;
    let target = null;
    let lastY = startY;
    let frame = 0;
    const body = document.body;
    const previousBody = { cursor: body.style.cursor, userSelect: body.style.userSelect, webkitUserSelect: body.style.webkitUserSelect };

    const autoScroll = () => {
      const scroller = scrollerRef.current;
      if (scroller) {
        const rect = scroller.getBoundingClientRect();
        if (lastY < rect.top + EDGE) scroller.scrollTop -= Math.ceil((rect.top + EDGE - lastY) / 4);
        else if (lastY > rect.bottom - EDGE) scroller.scrollTop += Math.ceil((lastY - (rect.bottom - EDGE)) / 4);
      }
      frame = requestAnimationFrame(autoScroll);
    };

    const onMove = (e) => {
      if (e.pointerId !== pointerId) return;
      lastY = e.clientY;
      if (!active) {
        if (Math.hypot(e.clientX - startX, e.clientY - startY) < DRAG_THRESHOLD) return;
        active = true;
        Object.assign(body.style, { cursor: "grabbing", userSelect: "none", webkitUserSelect: "none" });
        setDrag({ ...payload, x: e.clientX, y: e.clientY });
        frame = requestAnimationFrame(autoScroll);
      }
      e.preventDefault();
      if (ghostRef.current) ghostRef.current.style.transform = `translate(${e.clientX + 14}px, ${e.clientY + 10}px)`;
      target = resolveDrop(e.clientX, e.clientY, payload);
      setDropTarget(prev => (sameTarget(prev, target) ? prev : target));
    };

    const finish = (commit) => (e) => {
      if (e.pointerId !== pointerId) return;
      document.removeEventListener("pointermove", onMove);
      document.removeEventListener("pointerup", onUp);
      document.removeEventListener("pointercancel", onCancel);
      document.removeEventListener("keydown", onEscape, true);
      cancelAnimationFrame(frame);
      Object.assign(body.style, previousBody);
      if (active && commit && target) {
        if (payload.type === "row") moveRowTo(payload.index, target.index, target.after);
        else moveGroupTo(payload.groupPos, target.groupPos);
      }
      setDrag(null);
      setDropTarget(null);
    };
    const onUp = finish(true);
    const onCancel = finish(false);
    const onEscape = (e) => { if (e.key === "Escape") { e.preventDefault(); target = null; onCancel({ pointerId }); } };

    document.addEventListener("pointermove", onMove, { passive: false });
    document.addEventListener("pointerup", onUp);
    document.addEventListener("pointercancel", onCancel);
    document.addEventListener("keydown", onEscape, true);
  };

  // Keyboard fallback on a focused handle: ArrowUp / ArrowDown.
  const keyMoveRow = (event, index) => {
    if (event.key !== "ArrowUp" && event.key !== "ArrowDown") return;
    event.preventDefault();
    event.stopPropagation();
    const order = productOrder();
    const pos = order.indexOf(index);
    if (event.key === "ArrowUp" && pos > 0) moveRowTo(index, order[pos - 1], false);
    if (event.key === "ArrowDown" && pos >= 0 && pos < order.length - 1) moveRowTo(index, order[pos + 1], true);
  };
  const keyMoveGroup = (event, groupPos) => {
    if (event.key !== "ArrowUp" && event.key !== "ArrowDown") return;
    event.preventDefault();
    event.stopPropagation();
    moveGroupTo(groupPos, groupPos + (event.key === "ArrowUp" ? -1 : 1));
  };

  // Visual state helpers for table rows.
  const isDragging = (index) => drag && (drag.type === "row" ? drag.index === index : groupOf.get(index) === drag.groupPos);
  const rowDropClass = (index) => {
    if (!dropTarget) return "";
    if (dropTarget.kind === "row" && dropTarget.index === index) return dropTarget.after ? "dnd-drop-after" : "dnd-drop-before";
    if (dropTarget.kind === "group" && drag?.type === "group" && dropTarget.groupPos > drag.groupPos) {
      const group = groups[dropTarget.groupPos];
      if (group && group.indices[group.indices.length - 1] === index) return "dnd-drop-after";
    }
    return "";
  };
  const headingDropClass = (groupPos) =>
    dropTarget?.kind === "group" && drag?.type === "group" && dropTarget.groupPos === groupPos && groupPos < drag.groupPos ? "dnd-drop-before" : "";

  const ghost = drag ? createPortal(
    <div ref={ghostRef} className="invoice-dnd-ghost" style={{ transform: `translate(${drag.x + 14}px, ${drag.y + 10}px)` }} aria-hidden="true">
      {drag.type === "group" ? `Brand · ${drag.label}` : drag.label}
    </div>,
    document.body,
  ) : null;

  return {
    drag, groups, groupStart, groupOf, ghost,
    startRowDrag: (event, index, label) => startDrag(event, { type: "row", index, label }),
    startGroupDrag: (event, groupPos, label) => startDrag(event, { type: "group", groupPos, label }),
    keyMoveRow, keyMoveGroup, isDragging, rowDropClass, headingDropClass,
  };
}
