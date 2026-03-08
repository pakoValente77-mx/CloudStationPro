-- Poblar inicialmente la tabla de últimas mediciones con los datos más recientes de dcp_datos
INSERT INTO public.ultimas_mediciones (dcp_id, variable, ts, valor, tipo)
SELECT DISTINCT ON (dcp_id, variable) 
    dcp_id, 
    variable, 
    ts, 
    valor, 
    tipo
FROM public.dcp_datos
ORDER BY dcp_id, variable, ts DESC
ON CONFLICT (dcp_id, variable) DO UPDATE SET
    ts = EXCLUDED.ts,
    valor = EXCLUDED.valor,
    tipo = EXCLUDED.tipo;
