CREATE OR REPLACE FUNCTION public.trg_actualizar_estatus()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_rec_count INT;
    v_intervalo INT;
    v_status VARCHAR(20);
    v_window_start TIMESTAMP WITH TIME ZONE;
BEGIN
    v_window_start := NOW() - INTERVAL '48 hours';

    -- 1. Get the interval for this station (default to 60 if not found)
    SELECT COALESCE(intervalo_minutos, 60) INTO v_intervalo
    FROM public.estatus_estaciones
    WHERE dcp_id = NEW.dcp_id;

    IF v_intervalo IS NULL THEN v_intervalo := 60; END IF;

    -- 2. Count messages for THIS station only
    SELECT COUNT(*) INTO v_rec_count
    FROM public.bitacora_goes
    WHERE dcp_id = NEW.dcp_id
      AND timestamp_utc >= v_window_start;

    -- 3. Determine Color
    IF v_rec_count = 0 THEN
        v_status := 'NEGRO';
    ELSIF v_rec_count >= (48 * 60 / v_intervalo) THEN
        v_status := 'VERDE';
    ELSIF (48 * 60 / v_intervalo) - v_rec_count <= 2 THEN
        v_status := 'AMARILLO';
    ELSE
        v_status := 'ROJO';
    END IF;

    -- 4. Upsert Execution
    INSERT INTO public.estatus_estaciones (
        dcp_id, 
        fecha_calculo, 
        mensajes_recibidos_48h, 
        fecha_ultima_tx, 
        servidor, 
        color_estatus
    )
    VALUES (
        NEW.dcp_id, 
        NOW(), 
        v_rec_count, 
        NEW.timestamp_utc, 
        NEW.servidor, 
        v_status
    )
    ON CONFLICT (dcp_id) DO UPDATE SET
        fecha_calculo = EXCLUDED.fecha_calculo,
        mensajes_recibidos_48h = EXCLUDED.mensajes_recibidos_48h,
        fecha_ultima_tx = EXCLUDED.fecha_ultima_tx,
        servidor = EXCLUDED.servidor,
        color_estatus = EXCLUDED.color_estatus;

    RETURN NEW;
END;
$$;

-- 2. Create the Trigger
DROP TRIGGER IF EXISTS trg_update_estatus_on_insert ON public.bitacora_goes;

CREATE TRIGGER trg_update_estatus_on_insert
AFTER INSERT ON public.bitacora_goes
FOR EACH ROW
EXECUTE FUNCTION public.trg_actualizar_estatus();
