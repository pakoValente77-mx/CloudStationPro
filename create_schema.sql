-- 1. Create the status summary table
CREATE TABLE IF NOT EXISTS public.estatus_estaciones (
    dcp_id VARCHAR(20) PRIMARY KEY,
    fecha_calculo TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    intervalo_minutos INT DEFAULT 60, -- Default 60 mins (1 hour)
    mensajes_esperados_48h INT GENERATED ALWAYS AS (48 * 60 / intervalo_minutos) STORED, -- Auto-calc expected
    mensajes_recibidos_48h INT DEFAULT 0,
    color_estatus VARCHAR(20) DEFAULT 'NEGRO', -- VERDE, AMARILLO, ROJO, NEGRO
    fecha_ultima_tx TIMESTAMP WITH TIME ZONE,
    servidor VARCHAR(50)
);

-- Index for fast map querying
CREATE INDEX IF NOT EXISTS idx_estatus_color ON public.estatus_estaciones (color_estatus);

-- 2. Create the procedure to calculate status
CREATE OR REPLACE PROCEDURE public.actualizar_estatus_estaciones()
LANGUAGE plpgsql
AS $$
DECLARE
    v_window_start TIMESTAMP WITH TIME ZONE;
BEGIN
    v_window_start := NOW() - INTERVAL '48 hours';

    -- 1. Ensure all stations from last 7 days exist in status table
    INSERT INTO public.estatus_estaciones (dcp_id, intervalo_minutos)
    SELECT DISTINCT dcp_id, 60
    FROM public.bitacora_goes
    WHERE timestamp_utc > NOW() - INTERVAL '7 days'
    ON CONFLICT (dcp_id) DO NOTHING;

    -- 2. Update status for ALL stations based on 48h window
    WITH counts AS (
        SELECT 
            dcp_id,
            COUNT(*) as rec_count,
            MAX(timestamp_utc) as last_tx,
            (ARRAY_AGG(servidor ORDER BY timestamp_utc DESC))[1] as last_server
        FROM public.bitacora_goes
        WHERE timestamp_utc >= v_window_start
        GROUP BY dcp_id
    )
    UPDATE public.estatus_estaciones e
    SET 
        fecha_calculo = NOW(),
        mensajes_recibidos_48h = COALESCE(c.rec_count, 0),
        fecha_ultima_tx = c.last_tx,
        servidor = c.last_server,
        color_estatus = CASE 
            WHEN COALESCE(c.rec_count, 0) = 0 THEN 'NEGRO'
            WHEN COALESCE(c.rec_count, 0) >= (48 * 60 / e.intervalo_minutos) THEN 'VERDE'
            WHEN (48 * 60 / e.intervalo_minutos) - COALESCE(c.rec_count, 0) <= 2 THEN 'AMARILLO'
            ELSE 'ROJO'
        END
    FROM (SELECT dcp_id FROM public.estatus_estaciones) e2
    LEFT JOIN counts c ON e2.dcp_id = c.dcp_id
    WHERE e.dcp_id = e2.dcp_id;

END;
$$;
